using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue #273: deadlocks al insertar apuntes contables concurrentes (víctima 1205 en
    /// ContabilidadService.CrearLineas, ~10 casos entre el 29/06 y el 09/07). Un deadlock es
    /// transitorio (SQL Server revierte entera la transacción víctima y la otra continúa), así que
    /// la operación completa se reintenta con contexto/transacción nuevos, de forma acotada.
    /// </summary>
    [TestClass]
    public class ContabilidadServiceDeadlockTests
    {
        private const int SQL_DEADLOCK_VICTIM = 1205;

        [TestMethod]
        public void EsVictimaDeDeadlock_Sql1205AnidadoEnLaCadena_True()
        {
            // Los catch de ContabilidadService envuelven ("No se ha podido contabilizar el diario"),
            // así que el 1205 hay que buscarlo en la cadena de InnerException, no arriba.
            Exception ex = new Exception("No se ha podido contabilizar el diario",
                new Exception("intermedia", CrearSqlException(SQL_DEADLOCK_VICTIM)));

            Assert.IsTrue(ContabilidadService.EsVictimaDeDeadlock(ex));
        }

        [TestMethod]
        public void EsVictimaDeDeadlock_OtroErrorSql_False()
        {
            Exception ex = new Exception("wrap", CrearSqlException(2627)); // PK duplicada

            Assert.IsFalse(ContabilidadService.EsVictimaDeDeadlock(ex));
        }

        [TestMethod]
        public async Task ReintentarSiDeadlock_DeadlockTransitorio_ReintentaYDevuelve()
        {
            int intentos = 0;
            int resultado = await ContabilidadService.ReintentarSiDeadlock(() =>
            {
                intentos++;
                if (intentos < 3)
                {
                    throw new Exception("wrap", CrearSqlException(SQL_DEADLOCK_VICTIM));
                }
                return Task.FromResult(42);
            }, maxIntentos: 3, retrasoBaseMs: 0);

            Assert.AreEqual(42, resultado);
            Assert.AreEqual(3, intentos);
        }

        [TestMethod]
        public async Task ReintentarSiDeadlock_DeadlockPersistente_LanzaTrasAgotarIntentos()
        {
            int intentos = 0;
            try
            {
                _ = await ContabilidadService.ReintentarSiDeadlock<int>(() =>
                {
                    intentos++;
                    throw new Exception("wrap", CrearSqlException(SQL_DEADLOCK_VICTIM));
                }, maxIntentos: 3, retrasoBaseMs: 0);
                Assert.Fail("Debería haber lanzado tras agotar los intentos");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ContabilidadService.EsVictimaDeDeadlock(ex), "Debe propagar el deadlock original");
            }
            Assert.AreEqual(3, intentos);
        }

        [TestMethod]
        public async Task ReintentarSiDeadlock_ErrorQueNoEsDeadlock_NoReintenta()
        {
            int intentos = 0;
            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                ContabilidadService.ReintentarSiDeadlock<int>(() =>
                {
                    intentos++;
                    throw new InvalidOperationException("otro error");
                }, maxIntentos: 3, retrasoBaseMs: 0));

            Assert.AreEqual(1, intentos);
        }

        // ----- #296: el error de negocio del SP no debe quedar enterrado en ruido de transacciones -----

        [TestMethod]
        public void ComponerMensajeSinRuidoDeTransacciones_FiltraElRecuentoYConservaElNegocio()
        {
            // Caso real (Reina, 14/07): prdLiquidar aborta con RAISERROR y el SqlException llega
            // con los errores 266 (recuento BEGIN/COMMIT) por medio, ocultando la causa.
            var errores = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>
            {
                new System.Collections.Generic.KeyValuePair<int, string>(50000, "No se ha podido liquidar el movimiento del extracto del cliente. Cliente 35615"),
                new System.Collections.Generic.KeyValuePair<int, string>(266, "El recuento de transacciones después de EXECUTE indica un número no coincidente de instrucciones BEGIN y COMMIT."),
                new System.Collections.Generic.KeyValuePair<int, string>(266, "El recuento de transacciones después de EXECUTE indica un número no coincidente de instrucciones BEGIN y COMMIT."),
                new System.Collections.Generic.KeyValuePair<int, string>(50000, "Importes con mismo signo o importe 0.Cliente 35615")
            };

            string mensaje = ContabilidadService.ComponerMensajeSinRuidoDeTransacciones(errores);

            StringAssert.Contains(mensaje, "No se ha podido liquidar");
            StringAssert.Contains(mensaje, "Importes con mismo signo");
            Assert.IsFalse(mensaje.Contains("recuento de transacciones"), "El ruido del desajuste de transacciones no debe llegar al usuario");
        }

        [TestMethod]
        public void ComponerMensajeSinRuidoDeTransacciones_SoloRuido_DevuelveElMensajeGenerico()
        {
            var errores = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>
            {
                new System.Collections.Generic.KeyValuePair<int, string>(266, "El recuento de transacciones...")
            };

            string mensaje = ContabilidadService.ComponerMensajeSinRuidoDeTransacciones(errores);

            Assert.AreEqual("Error al contabilizar el diario", mensaje);
        }

        /// <summary>
        /// SqlException no tiene constructor público: se fabrica por reflection (SqlError interno +
        /// SqlErrorCollection interna + SqlException.CreateException), truco estándar para tests.
        /// </summary>
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
