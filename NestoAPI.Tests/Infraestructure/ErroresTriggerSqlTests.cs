using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Data.Entity.Core;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#473: los errores de los triggers de validación (IBAN, CIF repetido) salían como
    /// 500 a ELMAH; el usuario reintentaba sin saber qué corregir (ocho veces en un minuto).
    /// </summary>
    [TestClass]
    public class ErroresTriggerSqlTests
    {
        private const string MENSAJE_IBAN = "Dígito de control del IBAN incorrecto en el CCC número 1 del cliente 38077 (contacto 2)";
        private const string MENSAJE_CIF = "Ya existe un cliente con ese CIF/NIF";
        private const string MENSAJE_3609 = "La transacción terminó en el desencadenador. Se anuló el lote.";

        private static DbUpdateException ErrorDeTrigger(string mensaje)
        {
            // Lo que llega de verdad: DbUpdateException > UpdateException > SqlException con DOS
            // errores, el del RAISERROR (50000) y el 3609 que lo acompaña.
            return new DbUpdateException("An error occurred while updating the entries.",
                new UpdateException("An error occurred while updating the entries.",
                    CrearSqlException((ErroresTriggerSql.NUMERO_ERROR_DE_USUARIO, mensaje), (3609, MENSAJE_3609))));
        }

        [TestMethod]
        public void MensajeDeValidacion_ErrorDeTrigger_DevuelveSoloElTextoDelTrigger()
        {
            string mensaje = ErroresTriggerSql.MensajeDeValidacion(ErrorDeTrigger(MENSAJE_IBAN));

            Assert.AreEqual(MENSAJE_IBAN, mensaje, "sin el 3609 y a cualquier profundidad de InnerException");
        }

        [TestMethod]
        public void MensajeDeValidacion_ClaveForaneaRota_NoEsDeValidacion_Null()
        {
            // Un 547 (FK) es un fallo nuestro, no un motivo para el usuario: sigue siendo 500.
            var ex = new DbUpdateException("x", CrearSqlException((547, "The INSERT statement conflicted with the FOREIGN KEY constraint")));

            Assert.IsNull(ErroresTriggerSql.MensajeDeValidacion(ex));
        }

        [TestMethod]
        public void MensajeDeValidacion_SinSqlExceptionDentro_Null()
        {
            Assert.IsNull(ErroresTriggerSql.MensajeDeValidacion(new InvalidOperationException("otra cosa")));
            Assert.IsNull(ErroresTriggerSql.MensajeDeValidacion(null));
        }

        [TestMethod]
        public async Task PutCliente_ElTriggerDelCifRepetido_Devuelve400ConElMotivo()
        {
            var gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).Throws(ErrorDeTrigger(MENSAJE_CIF));
            var controller = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, A.Fake<NVEntities>());

            var resultado = await controller.PutCliente(new ClienteCrear()) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado, "antes era un 500");
            Assert.AreEqual(MENSAJE_CIF, resultado.Message);
        }

        [TestMethod]
        public async Task PutCliente_OtroErrorDeBaseDeDatos_SigueSiendo500()
        {
            var gestor = A.Fake<IGestorClientes>();
            var fk = new DbUpdateException("x", CrearSqlException((547, "FOREIGN KEY")));
            A.CallTo(() => gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).Throws(fk);
            var controller = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, A.Fake<NVEntities>());

            await Assert.ThrowsExceptionAsync<DbUpdateException>(() => controller.PutCliente(new ClienteCrear()));
        }

        [TestMethod]
        public async Task PutCCCs_ElTriggerDelIban_Devuelve400ConElMotivo()
        {
            var db = A.Fake<NVEntities>();
            A.CallTo(() => db.SaveChangesAsync()).Throws(ErrorDeTrigger(MENSAJE_IBAN));
            var gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.GuardarCCCs(A<NVEntities>._, A<GuardarCCCsRequest>._, A<string>._)).Returns(new GuardarCCCsRespuesta());
            var controller = new ClientesController(gestor, A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, db);

            var resultado = await controller.PutCCCs(new GuardarCCCsRequest { empresa = "1", cliente = "38077", contacto = "2" }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado, "antes era un 500 con DbUpdateException");
            Assert.AreEqual(MENSAJE_IBAN, resultado.Message);
        }

        // Mismo helper de reflexión que DiagnosticoBloqueosTests: SqlException no tiene constructor
        // público. Aquí además se fija el texto de cada error, que es lo que se traduce.
        private static SqlException CrearSqlException(params (int Number, string Message)[] errores)
        {
            ConstructorInfo ctorError = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .First(c => c.GetParameters().Length >= 7 && c.GetParameters()[0].ParameterType == typeof(int));

            var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true);
            MethodInfo add = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach ((int number, string message) in errores)
            {
                object[] args = ctorError.GetParameters().Select((p, i) =>
                {
                    if (i == 0) return (object)number;
                    if (p.ParameterType == typeof(string)) return p.Name == "errorMessage" ? message : "test";
                    if (p.ParameterType == typeof(byte)) return (byte)0;
                    if (p.ParameterType == typeof(int)) return 0;
                    if (p.ParameterType == typeof(uint)) return 0u;
                    return null;
                }).ToArray();
                _ = add.Invoke(collection, new object[] { ctorError.Invoke(args) });
            }

            MethodInfo crear = typeof(SqlException)
                .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(SqlErrorCollection), typeof(string) }, null);
            return (SqlException)crear.Invoke(null, new object[] { collection, "11.0.0" });
        }
    }
}
