using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#308: Pub/Sub entrega at-least-once; dos redeliveries concurrentes del mismo
    /// messageId pasaban ambos por la rama "no existe" de RecordAttempt (read-then-insert) y el
    /// segundo violaba PK_SyncMessageRetries tirando el webhook con 500.
    /// </summary>
    [TestClass]
    public class MessageRetryManagerTests
    {
        [TestMethod]
        public void EsClaveDuplicada_Sql2627AnidadaEnLaCadena_True()
        {
            // EF envuelve la SqlException en DbUpdateException(UpdateException(SqlException))
            var ex = new DbUpdateException("wrap", new Exception("intermedia", CrearSqlException(2627)));

            Assert.IsTrue(MessageRetryManager.EsClaveDuplicada(ex));
        }

        [TestMethod]
        public void EsClaveDuplicada_Sql2601IndiceUnico_True()
        {
            var ex = new DbUpdateException("wrap", CrearSqlException(2601));

            Assert.IsTrue(MessageRetryManager.EsClaveDuplicada(ex));
        }

        [TestMethod]
        public void EsClaveDuplicada_OtroErrorSql_False()
        {
            var ex = new DbUpdateException("wrap", CrearSqlException(1205)); // deadlock

            Assert.IsFalse(MessageRetryManager.EsClaveDuplicada(ex));
        }

        [TestMethod]
        public void EsClaveDuplicada_SinSqlExceptionEnLaCadena_False()
        {
            var ex = new DbUpdateException("wrap", new InvalidOperationException());

            Assert.IsFalse(MessageRetryManager.EsClaveDuplicada(ex));
        }

        [TestMethod]
        public async Task RecordAttempt_ClaveDuplicadaPorRedeliveryConcurrente_PasaPorLaRamaDeYaExiste()
        {
            // Arrange: el primer FirstOrDefault no ve el registro (lo insertará "otro" webhook
            // concurrente), el SaveChanges viola la PK, y la relectura ya lo encuentra.
            var lista = new List<SyncMessageRetry>();
            NVEntities db = A.Fake<NVEntities>();
            DbSet<SyncMessageRetry> fakeRetries = A.Fake<DbSet<SyncMessageRetry>>(o =>
                o.Implements<IQueryable<SyncMessageRetry>>().Implements<IDbAsyncEnumerable<SyncMessageRetry>>());
            A.CallTo(() => db.SyncMessageRetries).Returns(fakeRetries);
            ConfigurarFakeDbSet(fakeRetries, lista.AsQueryable());

            var insertadoPorElOtro = new SyncMessageRetry
            {
                MessageId = "m1",
                AttemptCount = 1,
                Status = RetryStatus.Retrying.ToString()
            };

            int saves = 0;
            A.CallTo(() => db.SaveChangesAsync()).ReturnsLazily(() =>
            {
                saves++;
                if (saves == 1)
                {
                    // El otro redelivery "gana" la inserción y este SaveChanges viola la PK
                    lista.Add(insertadoPorElOtro);
                    throw new DbUpdateException("dup", new Exception("intermedia", CrearSqlException(2627)));
                }
                return Task.FromResult(1);
            });

            var manager = new MessageRetryManager(db);

            // Act: no debe propagar la excepción
            await manager.RecordAttempt("m1", null);

            // Assert: se releyó el registro del otro webhook y se incrementó su contador
            Assert.AreEqual(2, saves);
            Assert.AreEqual(2, insertadoPorElOtro.AttemptCount);
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        // Mismo helper de reflexión que ContabilidadServiceDeadlockTests: SqlException no tiene
        // constructor público.
        private static SqlException CrearSqlException(int number)
        {
            ConstructorInfo ctorError = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .First(c => c.GetParameters().Length >= 7 && c.GetParameters()[0].ParameterType == typeof(int));
            object[] argsError = ctorError.GetParameters().Select((p, i) =>
            {
                if (i == 0) return (object)number;
                if (p.ParameterType == typeof(string)) return "test";
                if (p.ParameterType == typeof(byte)) return (byte)0;
                if (p.ParameterType == typeof(int)) return 0;
                if (p.ParameterType == typeof(uint)) return 0u;
                return null;
            }).ToArray();
            var error = (SqlError)ctorError.Invoke(argsError);

            var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true);
            _ = typeof(SqlErrorCollection)
                .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(collection, new object[] { error });

            MethodInfo crear = typeof(SqlException)
                .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(SqlErrorCollection), typeof(string) }, null);
            return (SqlException)crear.Invoke(null, new object[] { collection, "11.0.0" });
        }
    }
}
