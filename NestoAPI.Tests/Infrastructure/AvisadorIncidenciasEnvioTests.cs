using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#666 (Sugerencia 672 de Novedades): aviso a los comerciales del pedido cuando la agencia pone
    /// una incidencia en el envío. Correo (con copia a administración) y campana de Nesto.
    /// </summary>
    [TestClass]
    public class AvisadorIncidenciasEnvioTests
    {
        private static EnviosAgencia Envio() => new EnviosAgencia
        {
            Numero = 250001,
            Empresa = "1",
            Agencia = 9,
            Pedido = 925137,
            Cliente = "15191",
            Nombre = "Nombre del envío",
            CodigoBarras = "ALB123  ",
            Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
            DetalleEstado = "DIRECCIÓN INCORRECTA"
        };

        private static ComercialesPedido Comerciales() => new ComercialesPedido
        {
            Empresa = "1",
            Pedido = 925137,
            Cliente = "15191",
            NombreCliente = "Peluquería Ejemplo",
            UsuarioPedido = "MariaJose",
            CorreoUsuarioPedido = "mariajose@nuevavision.es",
            Comerciales =
            {
                new ComercialPedido { Vendedor = "SAN", Nombre = "Santiago", Correo = "santiago@nuevavision.es", Usuarios = { "Santiago" } },
                new ComercialPedido { Vendedor = "PEL", Nombre = "Peluquera", Correo = "peluqueria@nuevavision.es", Usuarios = { "Peluquera" }, EsPeluqueria = true }
            }
        };

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_INCIDENTADO, true, DisplayName = "Tramitado → Incidentado: avisa")]
        [DataRow(Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_INCIDENTADO, false, DisplayName = "Ya incidentado: no avisa")]
        [DataRow(Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_ENTREGADO, false, DisplayName = "Entregado: no avisa")]
        [DataRow(Constantes.Agencias.ESTADO_INCIDENTADO, Constantes.Agencias.ESTADO_TRAMITADO, false, DisplayName = "Se resuelve: no avisa")]
        public void PasaAIncidencia_SoloEnLaTransicion(short anterior, short nuevo, bool esperado)
        {
            Assert.AreEqual(esperado, AvisadorIncidenciasEnvio.PasaAIncidencia(anterior, new EnviosAgencia { Estado = nuevo }));
        }

        [TestMethod]
        public void ComponerCorreo_ALosComerciales_ConCopiaAAdministracion_YLosDatosDelEnvio()
        {
            MailMessage mail = AvisadorIncidenciasEnvio.ComponerCorreo(Envio(), Comerciales(), "CTT");

            CollectionAssert.AreEquivalent(new[] { "santiago@nuevavision.es", "peluqueria@nuevavision.es" }, mail.To.Select(t => t.Address).ToList());
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.CC.Single().Address);
            Assert.AreEqual("Incidencia de la agencia en el pedido 925137 - c/ 15191 - Peluquería Ejemplo", mail.Subject);
            StringAssert.Contains(mail.Body, "925137");
            StringAssert.Contains(mail.Body, System.Net.WebUtility.HtmlEncode("15191 - Peluquería Ejemplo"));
            StringAssert.Contains(mail.Body, "CTT");
            StringAssert.Contains(mail.Body, "ALB123");
            StringAssert.Contains(mail.Body, System.Net.WebUtility.HtmlEncode("DIRECCIÓN INCORRECTA"));
            StringAssert.Contains(mail.Body, "Revise el envío", "De usted");
        }

        [TestMethod]
        public void ComponerCorreo_SinNadieConCorreo_VaAAdministracion()
        {
            var comerciales = new ComercialesPedido { Comerciales = { new ComercialPedido { Vendedor = "SIN" } } };

            MailMessage mail = AvisadorIncidenciasEnvio.ComponerCorreo(Envio(), comerciales, "CTT");

            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.To.Single().Address);
            Assert.AreEqual(0, mail.CC.Count);
        }

        [TestMethod]
        public async Task AvisarAsync_CorreoYCampanaDeCadaComercial()
        {
            NVEntities db = A.Fake<NVEntities>();
            var agencias = new List<AgenciaTransporte> { new AgenciaTransporte { Numero = 9, Nombre = "CTT  " } }.AsQueryable();
            DbSet<AgenciaTransporte> fakeAgencias = A.Fake<DbSet<AgenciaTransporte>>(o => o.Implements<IQueryable<AgenciaTransporte>>());
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).Provider).Returns(agencias.Provider);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).Expression).Returns(agencias.Expression);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).ElementType).Returns(agencias.ElementType);
            A.CallTo(() => ((IQueryable<AgenciaTransporte>)fakeAgencias).GetEnumerator()).ReturnsLazily(() => agencias.GetEnumerator());
            A.CallTo(() => db.AgenciasTransportes).Returns(fakeAgencias);
            var lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer("1", 925137)).Returns(Comerciales());
            var correo = A.Fake<IServicioCorreoElectronico>();
            var notificaciones = A.Fake<IServicioNotificacionesPush>();

            await new AvisadorIncidenciasEnvio(db, lector, correo, notificaciones).AvisarAsync(Envio());

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To.Count == 2 && m.Body.Contains("CTT"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santiago", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisadorIncidenciasEnvio.TIPO_NOTIFICACION && n.Datos["pedido"] == "925137")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Peluquera", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AvisarAsync_SiFallaLeerLosComerciales_NoLanza()
        {
            NVEntities db = A.Fake<NVEntities>();
            var lector = A.Fake<ILectorComercialesPedido>();
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).Throws(new System.InvalidOperationException("BD caída"));
            var correo = A.Fake<IServicioCorreoElectronico>();

            await new AvisadorIncidenciasEnvio(db, lector, correo, A.Fake<IServicioNotificacionesPush>()).AvisarAsync(Envio());

            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }
    }
}
