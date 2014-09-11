﻿namespace TableDsl.Parser

open Basis.Core
open FParsec
open TableDsl
open TableDsl.Parser
open TableDsl.Parser.Types
open TableDsl.Parser.Primitives

module internal Impl =
  let pQuotedTypeParamElem = parse {
    do! pSkipToken "`"
    let! elem = many1Chars (noneOf "`")
    do! pSkipToken "`"
    return elem
  }

  let pNonQuotedTypeParamElem =
    let rec pNonQuotedTypeParamElem terminators : Parser<string> =
      let p = parse {
        let! ch = anyChar
        return!
          match ch with
          | '(' -> parse {
                     let! result = pNonQuotedTypeParamElem [')']
                     do! pchar ')' |>> ignore
                     return "(" + result + ")" }
          | x when List.exists ((=)x) terminators -> pzero
          | x -> preturn (string x)
      }
      many (attempt p) |>> (fun xs -> System.String.Concat(xs))
    pNonQuotedTypeParamElem [','; ')']

  let pBoundTypeParamElem = (pQuotedTypeParamElem <|> pNonQuotedTypeParamElem) >>= (function "" -> pzero | other -> preturn other)

  let pBoundTypeParam =
    sepBy pBoundTypeParamElem (pchar ',') |> between (pchar '(') (pchar ')')

  // TODO : '文字列'や4.2に対応すること
  let pOpenTypeParamElem =
    (regex "@[a-zA-Z0-9_]+" |>> fun x -> TypeVariable x) <|> (regex "[a-zA-Z0-9_]+" |>> fun x -> BoundValue x)

  let pOpenTypeParam =
    sepBy pOpenTypeParamElem (pSkipToken ",") |> between (pchar '(') (pchar ')')

  let resolveType typeName typeParams env =
    match env |> List.tryFind (fun (name, paramCount, _) -> name = typeName && paramCount = (List.length typeParams)) with
    | Some (name, paramCount, t) ->
        let typ = { ColumnSummary = None; ColumnTypeDef = t; ColumnJpName = None; ColumnAttributes = [] }
        typ, preturn ()
    | None -> Unchecked.defaultof<_>, failFatally (sprintf "%sという型が見つかりませんでした。" typeName)

  let pClosedTypeRefWithoutAttributes = parse {
    let! typeName = pName
    let! typeParams = opt (attempt pBoundTypeParam)
    let typeParams =
      match typeParams with
      | None -> []
      | Some x -> x
    let! env = getUserState
    let typ, perror = resolveType typeName typeParams env
    do! perror
    return ({ Type = typ; TypeParameters = typeParams }, [])
  }

  let pOpenTypeRefWithoutAttributes = parse {
    let! typeName = pName
    let! typeParams = opt (attempt pOpenTypeParam)
    let typeParams =
      match typeParams with
      | None -> []
      | Some x -> x
    let! env = getUserState
    let typ, perror = resolveType typeName typeParams env
    let typDef =
      match typ.ColumnTypeDef with
      | BuiltinType ({ TypeParameters = ts } as bt) ->
          BuiltinType { bt with TypeParameters = (typeParams, ts) ||> List.map2 (fun t1 t2 -> match t1 with BoundValue v -> BoundValue v | _ -> t2) }
      | AliasDef (({ TypeParameters = ts } as ad), org) ->
          AliasDef ({ ad with TypeParameters = (typeParams, ts) ||> List.map2 (fun t1 t2 -> match t1 with BoundValue v -> BoundValue v | _ -> t2) }, org)
      | other -> other
    do! perror
    return ({ typ with ColumnTypeDef = typDef }, [])
  }

  let pComplexAttribute = parse {
    do! wsnl |>> ignore
    let! name = pName
    do! pSkipToken "="
    let! value = regex "[a-zA-Z0-9_.]+"
    return ComplexAttr (name, { Value = value })
  }
  let pSimpleAttribute = wsnl >>. pName |>> (fun name -> SimpleAttr name)
  let pAttribute = attempt pComplexAttribute <|> pSimpleAttribute
  let pAttributes = sepBy1 pAttribute (pchar ';')

  let pClosedTypeRefWithAttributes = parse {
    do! pSkipToken "{"
    let! typ, _ = pClosedTypeRefWithoutAttributes
    do! pSkipToken "with"
    let! attrs = pAttributes
    do! pSkipToken "}"
    return (typ, attrs)
  }

  let pOpenTypeRefWithAttributes = parse {
    do! pSkipToken "{"
    let! typ, _ = pOpenTypeRefWithoutAttributes
    do! pSkipToken "with"
    let! attrs = pAttributes
    do! pSkipToken "}"
    return (typ, attrs)
  }

  let pClosedTypeRef = pClosedTypeRefWithAttributes <|> pClosedTypeRefWithoutAttributes
  let pOpenTypeRef = pOpenTypeRefWithAttributes <|> pOpenTypeRefWithoutAttributes

  let pAliasDef name typeParams = parse {
    let! body, attrs = pOpenTypeRef
    return (AliasDef ({ TypeName = name; TypeParameters = typeParams }, body), attrs)
  }

  let pEnumTypeDef name = parse {
    return! pzero
  }

  let pTypeDef name typeParams = pEnumTypeDef name <|> pAliasDef name typeParams

  let pTypeParams =
    sepBy (regex "@[a-zA-Z0-9_]+") (pSkipToken ",")
    |> between (pSkipToken "(") (pSkipToken ")")

  let checkColTypeParams name xs =
    let rec tryFindDup existsParams = function
    | x::_ when List.exists ((=)x) existsParams -> Some x
    | x::xs -> tryFindDup (x::existsParams) xs
    | [] -> None

    option {
      let! xs = xs
      let! dup = tryFindDup [] xs
      return failFatally (sprintf "型%sの定義で型変数%sが重複しています。" name dup)
    }

  let pColTypeDef = parse {
    let! colTypeSummary = pSummaryOpt
    do! pSkipToken "coltype"
    let! colTypeName = pColTypeName
    let! colTypeParams = pTypeParams |> attempt |> opt
    do! match checkColTypeParams colTypeName colTypeParams with Some p -> p | None -> preturn ()
    let colTypeParams =
      colTypeParams
      |> function Some x -> x | _ -> []
      |> List.map (fun p -> TypeVariable p)
    let! colTypeJpName = pJpNameOpt
    do! pSkipToken "="
    let! typ, attrs = pTypeDef colTypeName colTypeParams
    return ColTypeDef { ColumnSummary = colTypeSummary; ColumnTypeDef = typ; ColumnJpName = colTypeJpName; ColumnAttributes = attrs }
  }

  let pColumnDef = parse {
    let! colSummary = pSummaryOpt
    do! wsnl |>> ignore
    let! colName = pColName
    let! colJpName =
      if colName = "_" then preturn None else pJpNameOpt
    do! pSkipToken ":"
    let! colType, attrs = pClosedTypeRef
    return { ColumnSummary = colSummary
             ColumnName = if colName = "_" then Wildcard else ColumnName (colName, colJpName)
             ColumnType = (colType, attrs) }
  }

  let pTableDef = parse {
    let! tableSummary = pSummaryOpt
    do! pSkipToken "table"
    let! tableName = pTableName
    let! tableJpName = pJpNameOpt
    do! pSkipToken "="
    let! colDefs =
      sepEndBy (attempt pColumnDef) newline
      |> between (pSkipToken "{") (pSkipToken "}")
    return TableDef { TableSummary = tableSummary; TableName = tableName; TableJpName = tableJpName; ColumnDefs = colDefs }
  }

  // TODO : 一通り完成したら、エラーメッセージを入れる？(例: 「トップレベルの要素はcoltypeかtableのみが許されています。」)
  let parser = many (attempt pColTypeDef <|> pTableDef) .>> eof
