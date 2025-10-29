using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nika.Migrations;

public interface IMigrationSource
{
    Task<IReadOnlyCollection<Migration>> LoadMigrationsAsync(CancellationToken cancellationToken);
}
