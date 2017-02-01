using System;
using Eu.EDelivery.AS4.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Eu.EDelivery.AS4.UnitTests.Common
{
    /// <summary>
    /// Data Store Connection Test Setup
    /// </summary>
    public class GivenDatastoreFacts
    {
        protected readonly DbContextOptions<DatastoreContext> Options;

        protected Func<DatastoreContext> GetDataStoreContext { get; }
        /// <summary>
        /// Create a Default Datastore Facts
        /// </summary>
        public GivenDatastoreFacts()
        {
            this.Options = CreateNewContextOptions();
            GetDataStoreContext = () => new DatastoreContext(this.Options);
            Registry.Instance.CreateDatastoreContext = () => new DatastoreContext(this.Options);
        }

        private IServiceProvider _serviceProvider = null;

        protected void ResetInMemoryDatabase()
        {
            // Create a fresh service provider, and therefore a fresh 
            // InMemory database instance.
            _serviceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
        }

        protected DbContextOptions<DatastoreContext> CreateNewContextOptions()
        {
            if (this._serviceProvider == null)
            {
                ResetInMemoryDatabase();
            }

            // Create a new options instance telling the context to use an
            // InMemory database and the new service provider.
            var builder = new DbContextOptionsBuilder<DatastoreContext>();
            builder.UseInMemoryDatabase()
                .UseInternalServiceProvider(_serviceProvider);

            return builder.Options;
        }
    }
}