using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Infraestructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#662 (TNV#87): la preferencia «consejos y recordatorios de reposición» de cada dispositivo. Solo la mira la
    /// push de reposición; los avisos de pedidos, protocolos y cheques siguen llegando aunque esté apagada.
    /// </summary>
    [TestClass]
    public class RecibirConsejosTests
    {
        private NVEntities db;
        private List<DispositivoNotificacion> dispositivos;
        private List<DispositivoNotificacion> annadidos;
        private List<NotificacionBuzon> buzon;
        private ServicioNotificacionesPush servicio;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            dispositivos = new List<DispositivoNotificacion>();
            annadidos = new List<DispositivoNotificacion>();
            buzon = new List<NotificacionBuzon>();
            var fakeDispositivos = ServicioPedidosClienteTests.Fake(dispositivos);
            var fakeBuzon = ServicioPedidosClienteTests.Fake(new List<NotificacionBuzon>());
            A.CallTo(() => fakeDispositivos.Add(A<DispositivoNotificacion>._)).Invokes((DispositivoNotificacion d) => annadidos.Add(d));
            A.CallTo(() => fakeBuzon.Add(A<NotificacionBuzon>._)).Invokes((NotificacionBuzon n) => buzon.Add(n));
            A.CallTo(() => db.DispositivosNotificaciones).Returns(fakeDispositivos);
            A.CallTo(() => db.NotificacionesBuzon).Returns(fakeBuzon);
            servicio = new ServicioNotificacionesPush(() => db);
        }

        private static DispositivoNotificacion Dispositivo(string usuario, string token, bool consejos)
            => new DispositivoNotificacion
            {
                Usuario = usuario,
                Token = token,
                Empresa = "1",
                Cliente = "15191",
                Aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS,
                Plataforma = "Android",
                Activo = true,
                RecibirConsejos = consejos,
                FechaRegistro = new DateTime(2026, 10, 1),
                FechaUltimaActividad = new DateTime(2026, 10, 1)
            };

        private static RegistrarDispositivoDTO Registro(string token, bool? consejos)
            => new RegistrarDispositivoDTO
            {
                Token = token,
                Plataforma = "Android",
                Aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS,
                Empresa = "1",
                Cliente = "15191",
                RecibirConsejos = consejos
            };

        [TestMethod]
        public async Task Registrar_DispositivoNuevoSinDecirNada_NaceConConsejos()
        {
            _ = await servicio.RegistrarDispositivo(Registro("token-nuevo", null), "ana@cliente.es");

            Assert.IsTrue(annadidos.Single().RecibirConsejos);
        }

        [TestMethod]
        public async Task Registrar_SinValor_NoCambiaLoGuardado()
        {
            DispositivoNotificacion existente = Dispositivo("ana@cliente.es", "token", consejos: false);
            dispositivos.Add(existente);

            _ = await servicio.RegistrarDispositivo(Registro("token", null), "ana@cliente.es");

            Assert.IsFalse(existente.RecibirConsejos, "Una versión de la app que no lo manda no le enciende lo que apagó");
        }

        [TestMethod]
        public async Task Registrar_ConValor_LoGuarda()
        {
            DispositivoNotificacion existente = Dispositivo("ana@cliente.es", "token", consejos: true);
            dispositivos.Add(existente);

            _ = await servicio.RegistrarDispositivo(Registro("token", false), "ana@cliente.es");

            Assert.IsFalse(existente.RecibirConsejos);
        }

        [TestMethod]
        public async Task EnviarConsejoACliente_SoloLlegaALosDispositivosConConsejos()
        {
            dispositivos.Add(Dispositivo("ana@cliente.es", "token-ana", consejos: true));
            dispositivos.Add(Dispositivo("pepe@cliente.es", "token-pepe", consejos: false));

            _ = await servicio.EnviarConsejoACliente("1", "15191", new NotificacionPushDTO { Titulo = "Nueva Visión", Cuerpo = "x" });

            CollectionAssert.AreEqual(new[] { "ana@cliente.es" }, buzon.Select(b => b.Usuario).ToArray());
            Assert.AreEqual(Constantes.Aplicaciones.NESTO_TIENDAS, buzon.Single().Aplicacion);
        }

        [TestMethod]
        public async Task EnviarACliente_LosAvisosDeNegocioNoMiranLaPreferencia()
        {
            dispositivos.Add(Dispositivo("ana@cliente.es", "token-ana", consejos: false));

            _ = await servicio.EnviarACliente("1", "15191", new NotificacionPushDTO { Titulo = "Tu pedido ha salido" });

            Assert.AreEqual(1, buzon.Count);
        }
    }
}
