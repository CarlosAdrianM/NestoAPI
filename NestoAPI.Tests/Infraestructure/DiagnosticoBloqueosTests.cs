using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Filters;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#312: en los timeouts por bloqueo, decir QUIÉN bloquea para poder llamarle.
    /// </summary>
    [TestClass]
    public class DiagnosticoBloqueosTests
    {
        [TestMethod]
        public void EsErrorDeBloqueo_TimeoutAnidadoEnLaCadena_True()
        {
            // Los errores reales llegan envueltos ("No se pudo contabilizar" -> ... -> SqlException -2)
            var ex = new Exception("No se pudo contabilizar",
                new Exception("intermedia", CrearSqlException(-2)));

            Assert.IsTrue(DiagnosticoBloqueos.EsErrorDeBloqueo(ex));
        }

        [TestMethod]
        public void EsErrorDeBloqueo_LockRequestTimeout1222_True()
        {
            Assert.IsTrue(DiagnosticoBloqueos.EsErrorDeBloqueo(CrearSqlException(1222)));
        }

        [TestMethod]
        public void EsErrorDeBloqueo_OtroErrorSql_False()
        {
            Assert.IsFalse(DiagnosticoBloqueos.EsErrorDeBloqueo(CrearSqlException(2627)));
        }

        [TestMethod]
        public void EsErrorDeBloqueo_SinSqlException_False()
        {
            Assert.IsFalse(DiagnosticoBloqueos.EsErrorDeBloqueo(new Exception("otra cosa")));
        }

        [TestMethod]
        public void FormatearBloqueadores_ConexionDirectaDeNesto_UsaElLoginDeDominio()
        {
            // El caso real del 16/07/26: Santiago con Nesto directo y transacción abierta
            var filas = new List<DiagnosticoBloqueos.FilaBloqueador>
            {
                new DiagnosticoBloqueos.FilaBloqueador
                {
                    SessionId = 60,
                    LoginName = @"NUEVAVISION\Santiago",
                    ProgramName = "Nesto",
                    Desde = new DateTime(2026, 7, 16, 12, 3, 14),
                    Bloqueados = 3
                }
            };

            string resultado = DiagnosticoBloqueos.FormatearBloqueadores(filas);

            StringAssert.Contains(resultado, @"NUEVAVISION\Santiago");
            StringAssert.Contains(resultado, "Nesto");
            StringAssert.Contains(resultado, "12:03");
            StringAssert.Contains(resultado, "bloquea a 3");
        }

        [TestMethod]
        public void FormatearBloqueadores_ConexionDeLaApi_PrefiereElContextInfo()
        {
            // Las conexiones de la API van con la cuenta de servicio; el usuario real viene en
            // CONTEXT_INFO (interceptor de #286)
            var filas = new List<DiagnosticoBloqueos.FilaBloqueador>
            {
                new DiagnosticoBloqueos.FilaBloqueador
                {
                    LoginName = @"NUEVAVISION\RDS2016$",
                    ContextInfo = @"NUEVAVISION\Laura",
                    ProgramName = "NestoAPI"
                }
            };

            string resultado = DiagnosticoBloqueos.FormatearBloqueadores(filas);

            StringAssert.Contains(resultado, @"NUEVAVISION\Laura");
            Assert.IsFalse(resultado.Contains("RDS2016$"));
        }

        [TestMethod]
        public void FormatearBloqueadores_SinBloqueadores_DevuelveNull()
        {
            Assert.IsNull(DiagnosticoBloqueos.FormatearBloqueadores(new List<DiagnosticoBloqueos.FilaBloqueador>()));
            Assert.IsNull(DiagnosticoBloqueos.FormatearBloqueadores(null));
        }

        [TestMethod]
        public void AnadirBloqueosAlMensaje_ConDiagnostico_LoAnadeAlMensajeDelError()
        {
            var respuesta = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object> { ["message"] = "No se pudo contabilizar" }
            };

            GlobalExceptionFilter.AnadirBloqueosAlMensaje(respuesta, "Puede estar bloqueado por: X");

            var error = (Dictionary<string, object>)respuesta["error"];
            Assert.AreEqual("No se pudo contabilizar Puede estar bloqueado por: X", error["message"]);
        }

        [TestMethod]
        public void AnadirBloqueosAlMensaje_SinDiagnostico_NoTocaElMensaje()
        {
            var respuesta = new Dictionary<string, object>
            {
                ["error"] = new Dictionary<string, object> { ["message"] = "No se pudo contabilizar" }
            };

            GlobalExceptionFilter.AnadirBloqueosAlMensaje(respuesta, null);

            var error = (Dictionary<string, object>)respuesta["error"];
            Assert.AreEqual("No se pudo contabilizar", error["message"]);
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
