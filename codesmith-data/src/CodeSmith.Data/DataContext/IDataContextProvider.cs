using System.Linq;
#if !STANDALONE
using CodeSmith.Data.Future;
#endif

namespace CodeSmith.Data
{
    public interface IDataContextProvider
    {
        IDataContext GetDataConext(IQueryable query);

#if !STANDALONE
        IFutureContext GetFutureContext(IQueryable query);
#endif
    }
}