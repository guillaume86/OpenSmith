# CodeSmith.Data

Legacy data access library extracted from the [codesmithtools/Templates](https://github.com/codesmithtools/Templates) repository and ported to .NET 10. This library is used at runtime by code generated with PLINQO's LINQ to SQL templates.

## Projects

| Project | Description |
|---------|-------------|
| **CodeSmith.Data** | Base data access classes — rules, auditing, and resource management. Strong-named assembly |
| **CodeSmith.Data.LinqToSql** | LINQ to SQL extensions built on `CodeSmith.Data` — batch operations, future queries, and data context helpers. Strong-named assembly |
| **CodeSmith.Data.Memcached** | Memcached caching provider via Enyim.Caching |

## Notes

- This solution has no test project — coverage is provided indirectly through the PLINQO end-to-end tests.
