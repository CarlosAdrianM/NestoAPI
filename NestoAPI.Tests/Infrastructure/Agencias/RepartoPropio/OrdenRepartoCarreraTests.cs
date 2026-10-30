using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Revisión 30/10 (NestoAPI#759): dos PDA guardan a la vez el orden de una parada nueva; la segunda choca con la PK al insertar
    /// y antes salía un 500. Ahora se reintenta una vez solo el UPDATE (último gana por FechaHora).
    /// </summary>
    [TestClass]
    public class OrdenRepartoCarreraTests
    {
        [DataTestMethod]
        [DataRow(2627)]
        [DataRow(2601)]
        public async Task GuardarConReintento_ClaveDuplicadaAlInsertar_ReintentaSoloElUpdate(int numero)
        {
            var llamadas = new List<bool>();

            bool guardada = await RepositorioOrdenReparto.GuardarConReintento(soloActualizar =>
            {
                llamadas.Add(soloActualizar);
                if (!soloActualizar)
                {
                    throw CrearSqlException(numero);
                }
                return Task.FromResult(true);
            });

            Assert.IsTrue(guardada);
            CollectionAssert.AreEqual(new[] { false, true }, llamadas);
        }

        [TestMethod]
        public async Task GuardarConReintento_ClaveDuplicadaEnvuelta_TambienReintenta()
        {
            var llamadas = new List<bool>();

            bool guardada = await RepositorioOrdenReparto.GuardarConReintento(soloActualizar =>
            {
                llamadas.Add(soloActualizar);
                if (!soloActualizar)
                {
                    throw new InvalidOperationException("EF", CrearSqlException(2627));
                }
                return Task.FromResult(false);
            });

            Assert.IsFalse(guardada, "si la otra PDA tenía una FechaHora más nueva, gana ella");
            Assert.AreEqual(2, llamadas.Count);
        }

        [TestMethod]
        public async Task GuardarConReintento_ElReintentoTambienFalla_SePropaga()
        {
            int llamadas = 0;

            _ = await Assert.ThrowsExceptionAsync<SqlException>(() => RepositorioOrdenReparto.GuardarConReintento(_ =>
            {
                llamadas++;
                throw CrearSqlException(2627);
            }));

            Assert.AreEqual(2, llamadas, "solo un reintento");
        }

        [TestMethod]
        public async Task GuardarConReintento_OtroError_NoReintenta()
        {
            int llamadas = 0;

            _ = await Assert.ThrowsExceptionAsync<SqlException>(() => RepositorioOrdenReparto.GuardarConReintento(_ =>
            {
                llamadas++;
                throw CrearSqlException(547);
            }));

            Assert.AreEqual(1, llamadas);
        }

        [TestMethod]
        public async Task GuardarConReintento_SinError_UnaSolaLlamada()
        {
            var llamadas = new List<bool>();

            bool guardada = await RepositorioOrdenReparto.GuardarConReintento(soloActualizar =>
            {
                llamadas.Add(soloActualizar);
                return Task.FromResult(true);
            });

            Assert.IsTrue(guardada);
            CollectionAssert.AreEqual(new[] { false }, llamadas);
        }

        // Mismo helper de reflexión que DiagnosticoBloqueosTests: SqlException no tiene constructor público.
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
