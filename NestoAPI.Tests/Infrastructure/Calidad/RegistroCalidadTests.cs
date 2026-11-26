using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Transactions;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>NestoAPI#914: el único punto para apuntar señales de calidad nunca rompe ni retrasa a quien lo llama.</summary>
    [TestClass]
    public class RegistroCalidadTests
    {
        private IEscritorEventosCalidad escritor;
        private List<Exception> errores;
        private Func<IEscritorEventosCalidad> escritorAntes;
        private Action<Action> segundoPlanoAntes;
        private Action<Exception> registradorAntes;
        private Func<DateTime> ahoraAntes;

        [TestInitialize]
        public void Preparar()
        {
            escritorAntes = RegistroCalidad.Escritor;
            segundoPlanoAntes = RegistroCalidad.EnSegundoPlano;
            registradorAntes = RegistroCalidad.Registrador;
            ahoraAntes = RegistroCalidad.Ahora;
            escritor = A.Fake<IEscritorEventosCalidad>();
            errores = new List<Exception>();
            RegistroCalidad.Escritor = () => escritor;
            RegistroCalidad.EnSegundoPlano = trabajo => trabajo();
            RegistroCalidad.Registrador = errores.Add;
            RegistroCalidad.Ahora = () => new DateTime(2026, 11, 26, 10, 30, 0);
            RegistroCalidad.Reiniciar();
        }

        [TestCleanup]
        public void Limpiar()
        {
            RegistroCalidad.Escritor = escritorAntes;
            RegistroCalidad.EnSegundoPlano = segundoPlanoAntes;
            RegistroCalidad.Registrador = registradorAntes;
            RegistroCalidad.Ahora = ahoraAntes;
            RegistroCalidad.Reiniciar();
        }

        [TestMethod]
        public void Apuntar_EscribeLaFilaConUsuarioSinDominioYDetalleEnJson()
        {
            EventoCalidad escrito = null;
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Invokes((EventoCalidad e) => escrito = e);

            RegistroCalidad.Apuntar(TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING, @"NUEVAVISION\Laura", "1", 931726, "15191  ", "0  ",
                detalle: new { ModoActual = 2, ModoPedido = 1 });

            Assert.IsNotNull(escrito);
            Assert.AreEqual("CambioDeModoConPicking", escrito.Tipo);
            Assert.AreEqual("Laura", escrito.Usuario);
            Assert.AreEqual("1", escrito.Empresa);
            Assert.AreEqual(931726, escrito.Pedido);
            Assert.AreEqual("15191", escrito.Cliente);
            Assert.AreEqual("0", escrito.Contacto);
            Assert.AreEqual("{\"ModoActual\":2,\"ModoPedido\":1}", escrito.Detalle);
            Assert.AreEqual(new DateTime(2026, 11, 26, 10, 30, 0), escrito.Fecha);
            Assert.IsNull(escrito.Imputado);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void Apuntar_DetalleLargo_SeRecortaAMil()
        {
            EventoCalidad escrito = null;
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Invokes((EventoCalidad e) => escrito = e);

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro", detalle: new string('x', 3000));

            Assert.AreEqual(RegistroCalidad.LONGITUD_DETALLE, escrito.Detalle.Length);
        }

        [TestMethod]
        public void Apuntar_SinUsuario_ApuntaDesconocido()
        {
            EventoCalidad escrito = null;
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Invokes((EventoCalidad e) => escrito = e);

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, null);

            Assert.AreEqual("(desconocido)", escrito.Usuario);
        }

        [TestMethod]
        public void Apuntar_ElEscritorLanza_NoRompeYVaAElmah()
        {
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Throws(new InvalidOperationException("caída"));

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro", "1", 1);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "QuitarPicking");
        }

        [TestMethod]
        public void Apuntar_FaltaLaTabla_ElmahUnaSolaVez()
        {
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Throws(CrearSqlException(208, "Invalid object name 'dbo.CalidadEventos'."));

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro", "1", 1);
            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro", "1", 2);
            RegistroCalidad.Apuntar(TiposEventoCalidad.CAMBIO_DE_MODO_CON_PICKING, "Laura", "1", 3);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "Issue914_CalidadEventos.sql");
        }

        [TestMethod]
        public void Apuntar_ElSegundoPlanoLanza_NoRompe()
        {
            RegistroCalidad.EnSegundoPlano = _ => throw new InvalidOperationException("sin hilos");

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro");

            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Apuntar_NiElAvisoPuedeRomper()
        {
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Throws(new InvalidOperationException("caída"));
            RegistroCalidad.Registrador = _ => throw new InvalidOperationException("ELMAH caído");

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro");
        }

        [TestMethod]
        public void Apuntar_SinTipo_NoEscribeNada()
        {
            RegistroCalidad.Apuntar(" ", "Pedro");

            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Apuntar_DentroDeUnaTransaccion_EscribeFueraDeElla()
        {
            Transaction vista = Transaction.Current;
            A.CallTo(() => escritor.Escribir(A<EventoCalidad>._)).Invokes(() => vista = Transaction.Current);

            using (var transaccion = new TransactionScope())
            {
                RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro");
                Assert.IsNotNull(Transaction.Current);
            }

            Assert.IsNull(vista, "La escritura no debe alistarse en la transacción de quien apunta");
        }

        [TestMethod]
        public void Apuntar_SinCadenaDeConexion_NoHaceNada()
        {
            RegistroCalidad.Escritor = () => EscritorEventosCalidadSql.DesdeConfiguracion();

            RegistroCalidad.Apuntar(TiposEventoCalidad.QUITAR_PICKING, "Pedro");

            Assert.AreEqual(0, errores.Count);
        }

        private static SqlException CrearSqlException(int numero, string mensaje)
        {
            ConstructorInfo ctorError = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .First(c => c.GetParameters().Length >= 7 && c.GetParameters()[0].ParameterType == typeof(int));
            var coleccion = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true);
            MethodInfo anadir = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] argumentos = ctorError.GetParameters().Select((p, i) =>
            {
                if (i == 0) return (object)numero;
                if (p.ParameterType == typeof(string)) return p.Name == "errorMessage" ? mensaje : "test";
                if (p.ParameterType == typeof(byte)) return (byte)0;
                if (p.ParameterType == typeof(int)) return 0;
                if (p.ParameterType == typeof(uint)) return 0u;
                return null;
            }).ToArray();
            _ = anadir.Invoke(coleccion, new object[] { ctorError.Invoke(argumentos) });
            MethodInfo crear = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(SqlErrorCollection), typeof(string) }, null);
            return (SqlException)crear.Invoke(null, new object[] { coleccion, "11.0.0" });
        }
    }
}
