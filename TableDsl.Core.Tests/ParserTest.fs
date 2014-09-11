﻿namespace TableDsl.Core.Tests

open FsUnit
open NUnit.Framework

[<TestFixture>]
module ParserTest =
  open Basis.Core
  open TableDsl
  open TableDsl.Parser

  let builtin0 name =
    { ColumnTypeDef = BuiltinType { TypeName = name; TypeParameters = [] }
      ColumnAttributes = []; ColumnSummary = None; ColumnJpName = None }

  let builtin1 name _1 =
    { ColumnTypeDef = BuiltinType { TypeName = name; TypeParameters = [TypeVariable _1] }
      ColumnAttributes = []; ColumnSummary = None; ColumnJpName = None}

  let builtin2 name _1 _2 =
    { ColumnTypeDef = BuiltinType { TypeName = name; TypeParameters = [TypeVariable _1; TypeVariable _2] }
      ColumnAttributes = []; ColumnSummary = None; ColumnJpName = None}

  [<Test>]
  let ``empty string`` () =
    ""
    |> parse
    |> should equal []

  module ColTypeDef =
    let parse input =
      Parser.parse input
      |> List.choose (function ColTypeDef def -> Some def | _ -> None)

    let tryParse input =
      Parser.tryParse input
      |> Result.map (fst >> List.choose (function ColTypeDef def -> Some def | _ -> None))

    [<Test>]
    let ``one simple alias def`` () =
      "coltype Created = datetime2"
      |> parse
      |> should equal [ { ColumnTypeDef = AliasDef ({ TypeName = "Created"; TypeParameters = [] }, builtin0 "datetime2")
                          ColumnAttributes = []
                          ColumnSummary = None
                          ColumnJpName = None } ]

    [<Test>]
    let ``one alias def with attribute`` () =
      "coltype nvarchar = { nvarchar with collate = Japanese_BIN }"
      |> parse
      |> should equal [ { ColumnTypeDef = AliasDef ({ TypeName = "nvarchar"; TypeParameters = [] }, builtin0 "nvarchar")
                          ColumnAttributes = [ ComplexAttr ("collate", { Value = "Japanese_BIN" }) ]
                          ColumnSummary = None
                          ColumnJpName = None } ]

    [<Test>]
    let ``one generic alias def with attribute`` () =
      "coltype nvarchar(@n) = { nvarchar(@n) with collate = Japanese_BIN }"
      |> parse
      |> should equal [ { ColumnTypeDef = AliasDef ({ TypeName = "nvarchar"; TypeParameters = [TypeVariable "@n"] }, builtin1 "nvarchar" "@1")
                          ColumnAttributes = [ ComplexAttr ("collate", { Value = "Japanese_BIN" }) ]
                          ColumnSummary = None
                          ColumnJpName = None } ]

    [<Test>]
    let ``one generic alias def with attribute - partial bound`` () =
      let originalType =
        { ColumnTypeDef = BuiltinType { TypeName = "decimal"; TypeParameters = [TypeVariable "@1"; BoundValue "4"] }
          ColumnAttributes = []; ColumnSummary = None; ColumnJpName = None}

      "coltype T(@n) = decimal(@n, 4)"
      |> parse
      |> should equal [ { ColumnTypeDef = AliasDef ({ TypeName = "T"; TypeParameters = [TypeVariable "@n"] }, originalType)
                          ColumnAttributes = []
                          ColumnSummary = None
                          ColumnJpName = None } ]

    [<Test>]
    let ``summary and jp name`` () =
      """
      /// 名前を表します。
      coltype Name[名前] = { nvarchar with collate = Japanese_BIN }
      """
      |> parse
      |> should equal [ { ColumnTypeDef = AliasDef ({ TypeName = "Name"; TypeParameters = [] }, builtin0 "nvarchar")
                          ColumnAttributes = [ ComplexAttr ("collate", { Value = "Japanese_BIN" }) ]
                          ColumnSummary = Some "名前を表します。"
                          ColumnJpName = Some "名前" } ]

    [<Test>]
    let ``dup type variable`` () =
      "coltype nvarchar(@n, @n) = { nvarchar(@n) with collate = Japanese_BIN }"
      |> tryParse
      |> function
         | Success res -> failwithf "oops! Success! : %A" res
         | Failure (FParsecDefaultMessage err) -> failwithf "oops! : %s" err
         | Failure (UserFriendlyMessages errs) -> errs |> List.map (fun (_, _, msg) -> msg) |> should equal ["型nvarcharの定義で型変数@nが重複しています。"]

  module TableDef =
    let parse input =
      Parser.parse input
      |> List.choose (function TableDef def -> Some def | _ -> None)

    let tryParse input =
      Parser.tryParse input
      |> Result.map (fst >> List.choose (function TableDef def -> Some def | _ -> None))

    [<Test>]
    let ``one simple table`` () =
      """
      table Users = {
        Id: int
        Name: nvarchar(16)
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) }
                   { ColumnSummary = None
                     ColumnName = ColumnName ("Name", None)
                     ColumnType = ({ Type = builtin1 "nvarchar" "@1"; TypeParameters = [ "16" ] }, []) } ]
             }
           ]

    [<Test>]
    let ``two simple tables`` () =
      """
      table Users = {
        Id: int
        Name: nvarchar(16)
      }
      table DeletedUsers = {
        Id: int
        UserId: int
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) }
                   { ColumnSummary = None
                     ColumnName = ColumnName ("Name", None)
                     ColumnType = ({ Type = builtin1 "nvarchar" "@1"; TypeParameters = [ "16" ] }, []) } ]
             }
             { TableSummary = None
               TableName = "DeletedUsers"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) }
                   { ColumnSummary = None
                     ColumnName = ColumnName ("UserId", None)
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) } ] }
           ]

    [<Test>]
    let ``simple attr`` () =
      """
      table Users = {
        Id: { uniqueidentifier with PK }
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "uniqueidentifier"; TypeParameters = [] }, [SimpleAttr "PK"]) } ]
             }
           ]

    [<Test>]
    let ``complex attr`` () =
      """
      table Users = {
        Id: { uniqueidentifier with index = unclustered.IX1.1 }
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "uniqueidentifier"; TypeParameters = [] }, [ComplexAttr ("index", { Value = "unclustered.IX1.1" })]) } ]
             }
           ]

    [<Test>]
    let ``attributes`` () =
      """
      table Users = {
        Id: { uniqueidentifier with PK = unclustered; index = clustered.IX1.1; unique }
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "uniqueidentifier"; TypeParameters = [] },
                                   [ ComplexAttr ("PK", { Value = "unclustered" })
                                     ComplexAttr ("index", { Value = "clustered.IX1.1" })
                                     SimpleAttr "unique" ]) } ]
             }
           ]

    [<Test>]
    let ``wildcard column`` () =
      """
      table Users = {
        _ : datetime2
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = Wildcard
                     ColumnType = ({ Type = builtin0 "datetime2"; TypeParameters = [] }, []) } ] } ]

    [<Test>]
    let ``nullable`` () =
      """
      table Users = {
        Id: int
        Name: nullable(nvarchar(16))
        Age: nullable(int)
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = None
               TableName = "Users"
               TableJpName = None
               ColumnDefs =
                 [ { ColumnSummary = None
                     ColumnName = ColumnName ("Id", None)
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) }
                   { ColumnSummary = None
                     ColumnName = ColumnName ("Name", None)
                     ColumnType = ({ Type = builtin1 "nullable" "@type"
                                     TypeParameters = [ "nvarchar(16)" ] }, []) }
                   { ColumnSummary = None
                     ColumnName = ColumnName ("Age", None)
                     ColumnType = ({ Type = builtin1 "nullable" "@type"
                                     TypeParameters = [ "int" ] }, []) } ]
             }
           ]

    [<Test>]
    let ``summary and jp name`` () =
      """
      /// ユーザテーブル
      /// ユーザを表す。
      table Users[ユーザテーブル] = {
        /// ID
        Id[ID]: int
        /// ユーザ名
        Name[名前]: nvarchar(16)
      }
      """
      |> parse
      |> should equal
           [ { TableSummary = Some "ユーザテーブル\nユーザを表す。"
               TableName = "Users"
               TableJpName = Some "ユーザテーブル"
               ColumnDefs =
                 [ { ColumnSummary = Some "ID"
                     ColumnName = ColumnName ("Id", Some "ID")
                     ColumnType = ({ Type = builtin0 "int"; TypeParameters = [] }, []) }
                   { ColumnSummary = Some "ユーザ名"
                     ColumnName = ColumnName ("Name", Some "名前")
                     ColumnType = ({ Type = builtin1 "nvarchar" "@1"; TypeParameters = [ "16" ] }, []) } ]
             }
           ]

    let errMsg (lines: string list) = System.String.Join("\r\n", lines)

    [<Test>]
    let ``type not found`` () =
      """
      table Users = {
        Name: string
      }
      """
      |> tryParse
      |> function
         | Success res -> failwithf "oops! Success! : %A" res
         | Failure (FParsecDefaultMessage err) -> failwithf "oops! : %s" err
         | Failure (UserFriendlyMessages errs) -> errs |> List.map (fun (_, _, msg) -> msg) |> should equal ["stringという型が見つかりませんでした。"]
