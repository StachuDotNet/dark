import { PackageNode } from "../../types";

export class PackageDemoData {
  static getEnhancedPackagesData(): PackageNode[] {
    return [
      {
        id: "Darklang",
        label: "🏢 Darklang",
        type: "namespace",
        collapsibleState: 1,
        contextValue: "namespace",
        children: [
          {
            id: "Darklang.Stdlib",
            label: "📁 Stdlib",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "Darklang.Stdlib.List",
                label: "📁 List",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "Darklang.Stdlib.List.map",
                    label: "🔧 map",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.List.map",
                    packagePath: "Darklang.Stdlib.List.map"
                  },
                  {
                    id: "Darklang.Stdlib.List.filter",
                    label: "🔧 filter",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.List.filter",
                    packagePath: "Darklang.Stdlib.List.filter"
                  },
                  {
                    id: "Darklang.Stdlib.List.fold",
                    label: "🔧 fold",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.List.fold",
                    packagePath: "Darklang.Stdlib.List.fold"
                  }
                ]
              },
              {
                id: "Darklang.Stdlib.String",
                label: "📁 String",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "Darklang.Stdlib.String.length",
                    label: "🔧 length",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.String.length",
                    packagePath: "Darklang.Stdlib.String.length"
                  },
                  {
                    id: "Darklang.Stdlib.String.concat",
                    label: "🔧 concat",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.String.concat",
                    packagePath: "Darklang.Stdlib.String.concat"
                  }
                ]
              },
              {
                id: "Darklang.Stdlib.Option",
                label: "📁 Option",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "Darklang.Stdlib.Option.Option",
                    label: "📋 Option",
                    type: "type",
                    collapsibleState: 0,
                    contextValue: "type:Darklang.Stdlib.Option.Option",
                    packagePath: "Darklang.Stdlib.Option.Option"
                  },
                  {
                    id: "Darklang.Stdlib.Option.map",
                    label: "🔧 map",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Option.map",
                    packagePath: "Darklang.Stdlib.Option.map"
                  },
                  {
                    id: "Darklang.Stdlib.Option.withDefault",
                    label: "🔧 withDefault",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Option.withDefault",
                    packagePath: "Darklang.Stdlib.Option.withDefault"
                  },
                  {
                    id: "Darklang.Stdlib.Option.isSome",
                    label: "🔧 isSome",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Option.isSome",
                    packagePath: "Darklang.Stdlib.Option.isSome"
                  }
                ]
              },
              {
                id: "Darklang.Stdlib.Result",
                label: "📁 Result",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "Darklang.Stdlib.Result.Result",
                    label: "📋 Result",
                    type: "type",
                    collapsibleState: 0,
                    contextValue: "type:Darklang.Stdlib.Result.Result",
                    packagePath: "Darklang.Stdlib.Result.Result"
                  },
                  {
                    id: "Darklang.Stdlib.Result.map",
                    label: "🔧 map",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Result.map",
                    packagePath: "Darklang.Stdlib.Result.map"
                  },
                  {
                    id: "Darklang.Stdlib.Result.mapError",
                    label: "🔧 mapError",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Result.mapError",
                    packagePath: "Darklang.Stdlib.Result.mapError"
                  }
                ]
              },
              {
                id: "Darklang.Stdlib.Http",
                label: "📁 Http",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "Darklang.Stdlib.Http.get",
                    label: "🔧 get",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Http.get",
                    packagePath: "Darklang.Stdlib.Http.get"
                  },
                  {
                    id: "Darklang.Stdlib.Http.post",
                    label: "🔧 post",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:Darklang.Stdlib.Http.post",
                    packagePath: "Darklang.Stdlib.Http.post"
                  },
                  {
                    id: "Darklang.Stdlib.Http.Response",
                    label: "📋 Response",
                    type: "type",
                    collapsibleState: 0,
                    contextValue: "type:Darklang.Stdlib.Http.Response",
                    packagePath: "Darklang.Stdlib.Http.Response"
                  }
                ]
              }
            ]
          }
        ]
      },
      {
        id: "MyApp",
        label: "🌐 MyApp",
        type: "namespace",
        collapsibleState: 1,
        contextValue: "namespace",
        children: [
          {
            id: "MyApp.User",
            label: "📁 User",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "MyApp.User.User",
                label: "📋 User",
                type: "type",
                collapsibleState: 0,
                contextValue: "type:MyApp.User.User",
                packagePath: "MyApp.User.User"
              },
              {
                id: "MyApp.User.create",
                label: "🔧 create",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.User.create",
                packagePath: "MyApp.User.create"
              },
              {
                id: "MyApp.User.validate",
                label: "🔧 validate [NEW]",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.User.validate",
                packagePath: "MyApp.User.validate"
              }
            ]
          },
          {
            id: "MyApp.Auth",
            label: "📁 Auth",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "MyApp.Auth.login",
                label: "🔧 login",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Auth.login",
                packagePath: "MyApp.Auth.login"
              },
              {
                id: "MyApp.Auth.hashPassword",
                label: "🔧 hashPassword",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Auth.hashPassword",
                packagePath: "MyApp.Auth.hashPassword"
              },
              {
                id: "MyApp.Auth.verifyPassword",
                label: "🔧 verifyPassword",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Auth.verifyPassword",
                packagePath: "MyApp.Auth.verifyPassword"
              },
              {
                id: "MyApp.Auth.generateToken",
                label: "🔧 generateToken",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Auth.generateToken",
                packagePath: "MyApp.Auth.generateToken"
              }
            ]
          },
          {
            id: "MyApp.Database",
            label: "📁 Database",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "MyApp.Database.Connection",
                label: "📋 Connection",
                type: "type",
                collapsibleState: 0,
                contextValue: "type:MyApp.Database.Connection",
                packagePath: "MyApp.Database.Connection"
              },
              {
                id: "MyApp.Database.query",
                label: "🔧 query",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Database.query",
                packagePath: "MyApp.Database.query"
              },
              {
                id: "MyApp.Database.transaction",
                label: "🔧 transaction",
                type: "function",
                collapsibleState: 0,
                contextValue: "fn:MyApp.Database.transaction",
                packagePath: "MyApp.Database.transaction"
              }
            ]
          },
          {
            id: "MyApp.Api",
            label: "📁 Api",
            type: "module",
            collapsibleState: 1,
            contextValue: "module",
            children: [
              {
                id: "MyApp.Api.Users",
                label: "📁 Users",
                type: "module",
                collapsibleState: 1,
                contextValue: "module",
                children: [
                  {
                    id: "MyApp.Api.Users.get",
                    label: "🔧 get",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:MyApp.Api.Users.get",
                    packagePath: "MyApp.Api.Users.get"
                  },
                  {
                    id: "MyApp.Api.Users.create",
                    label: "🔧 create [MODIFIED]",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:MyApp.Api.Users.create",
                    packagePath: "MyApp.Api.Users.create"
                  },
                  {
                    id: "MyApp.Api.Users.update",
                    label: "🔧 update [CONFLICT]",
                    type: "function",
                    collapsibleState: 0,
                    contextValue: "fn:MyApp.Api.Users.update",
                    packagePath: "MyApp.Api.Users.update"
                  }
                ]
              }
            ]
          }
        ]
      }
    ];
  }
}