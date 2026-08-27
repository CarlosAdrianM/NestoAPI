using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    /// <summary>
    /// Buzón de notificaciones persistente (#387, lo que espera TiendasNuevaVision#36).
    /// Firebase no se inicializa en los tests, así que estos tests cubren justo el caso que da
    /// sentido al buzón: que la notificación quede guardada aunque la push no salga.
    /// </summary>
    [TestClass]
    public class BuzonNotificacionesTests
    {
        private NVEntities db;
        private DbSet<NotificacionBuzon> fakeBuzon;
        private DbSet<DispositivoNotificacion> fakeDispositivos;
        private ServicioNotificacionesPush servicio;

        private const string USUARIO = "cliente@ejemplo.com";
        private const string OTRO_USUARIO = "otro@ejemplo.com";
        private const string APLICACION = "NestoTiendas";

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeBuzon = A.Fake<DbSet<NotificacionBuzon>>(o => o.Implements<IQueryable<NotificacionBuzon>>().Implements<IDbAsyncEnumerable<NotificacionBuzon>>());
            fakeDispositivos = A.Fake<DbSet<DispositivoNotificacion>>(o => o.Implements<IQueryable<DispositivoNotificacion>>().Implements<IDbAsyncEnumerable<DispositivoNotificacion>>());

            A.CallTo(() => db.NotificacionesBuzon).Returns(fakeBuzon);
            A.CallTo(() => db.DispositivosNotificaciones).Returns(fakeDispositivos);

            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>().AsQueryable());
            ConfigurarFakeDbSet(fakeDispositivos, new List<DispositivoNotificacion>().AsQueryable());

            servicio = new ServicioNotificacionesPush(() => db);
        }

        private static DispositivoNotificacion Dispositivo(string usuario, string token, string cliente = "12345")
        {
            return new DispositivoNotificacion
            {
                Usuario = usuario,
                Token = token,
                Empresa = "1",
                Cliente = cliente,
                Aplicacion = APLICACION,
                Plataforma = "Android",
                Activo = true
            };
        }

        private static NotificacionBuzon EnBuzon(int id, string usuario, DateTime fecha, DateTime? leida = null, DateTime? eliminada = null, string datos = null, string aplicacion = APLICACION)
        {
            return new NotificacionBuzon
            {
                Id = id,
                Usuario = usuario,
                Aplicacion = aplicacion,
                Titulo = $"Notificación {id}",
                Cuerpo = "Cuerpo",
                Datos = datos,
                FechaCreacion = fecha,
                FechaLeida = leida,
                FechaEliminada = eliminada
            };
        }

        private List<NotificacionBuzon> CapturarAnnadidas()
        {
            List<NotificacionBuzon> annadidas = new List<NotificacionBuzon>();
            A.CallTo(() => fakeBuzon.Add(A<NotificacionBuzon>._))
                .Invokes((NotificacionBuzon n) => annadidas.Add(n));
            return annadidas;
        }

        [TestMethod]
        public async Task Buzon_SiFirebaseNoEnvia_LaNotificacionSeGuardaIgualmente()
        {
            ConfigurarFakeDbSet(fakeDispositivos, new List<DispositivoNotificacion> { Dispositivo(USUARIO, "token1") }.AsQueryable());
            List<NotificacionBuzon> annadidas = CapturarAnnadidas();

            int enviados = await servicio.EnviarACliente("1", "12345", new NotificacionPushDTO
            {
                Titulo = "Factura disponible",
                Cuerpo = "Ya tienes tu factura",
                Datos = new Dictionary<string, string> { { "tipo", "factura" } }
            });

            Assert.AreEqual(0, enviados, "Sin Firebase no sale ninguna push...");
            Assert.AreEqual(1, annadidas.Count, "...pero el buzón es justamente la red de seguridad");
            Assert.AreEqual("Factura disponible", annadidas[0].Titulo);
            Assert.AreEqual(USUARIO, annadidas[0].Usuario);
            Assert.AreEqual(APLICACION, annadidas[0].Aplicacion);
            Assert.AreEqual("12345", annadidas[0].Cliente);
            Assert.IsNull(annadidas[0].FechaLeida, "Nace sin leer");
            StringAssert.Contains(annadidas[0].Datos, "factura", "Los datos viajan como JSON para poder navegar desde el buzón");
        }

        [TestMethod]
        public async Task Buzon_ConVariosDispositivosDelMismoUsuario_GuardaUnaSolaFila()
        {
            // Móvil y tablet del mismo cliente: la notificación es una, y su estado de leída también.
            ConfigurarFakeDbSet(fakeDispositivos, new List<DispositivoNotificacion>
            {
                Dispositivo(USUARIO, "token-movil"),
                Dispositivo(USUARIO, "token-tablet")
            }.AsQueryable());
            List<NotificacionBuzon> annadidas = CapturarAnnadidas();

            _ = await servicio.EnviarACliente("1", "12345", new NotificacionPushDTO { Titulo = "Aviso" });

            Assert.AreEqual(1, annadidas.Count);
        }

        [TestMethod]
        public async Task Buzon_EnvioATodos_GuardaUnaFilaPorDestinatario()
        {
            ConfigurarFakeDbSet(fakeDispositivos, new List<DispositivoNotificacion>
            {
                Dispositivo(USUARIO, "token1", "12345"),
                Dispositivo(USUARIO, "token2", "12345"),
                Dispositivo(OTRO_USUARIO, "token3", "67890")
            }.AsQueryable());
            List<NotificacionBuzon> annadidas = CapturarAnnadidas();

            _ = await servicio.EnviarATodosDeAplicacion(APLICACION, new NotificacionPushDTO { Titulo = "Nuevo protocolo" });

            Assert.AreEqual(2, annadidas.Count, "Una fila por destinatario, para que leída/no leída sea individual");
            CollectionAssert.AreEquivalent(new[] { USUARIO, OTRO_USUARIO }, annadidas.Select(n => n.Usuario).ToArray());
        }

        [TestMethod]
        public async Task Buzon_SinDispositivosRegistrados_NoGuardaNada()
        {
            List<NotificacionBuzon> annadidas = CapturarAnnadidas();

            _ = await servicio.EnviarACliente("1", "99999", new NotificacionPushDTO { Titulo = "Aviso" });

            Assert.AreEqual(0, annadidas.Count);
        }

        [TestMethod]
        public async Task Buzon_DevuelveSoloLasDelUsuarioYSuAplicacion()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(2, OTRO_USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(3, USUARIO, new DateTime(2026, 8, 27), aplicacion: "NestoApp")
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 1, 20);

            Assert.AreEqual(1, buzon.Count);
            Assert.AreEqual(1, buzon[0].Id);
        }

        [TestMethod]
        public async Task Buzon_NoDevuelveLasEliminadas()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(2, USUARIO, new DateTime(2026, 8, 27), eliminada: new DateTime(2026, 8, 27))
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 1, 20);

            Assert.AreEqual(1, buzon.Count);
            Assert.AreEqual(1, buzon[0].Id);
        }

        [TestMethod]
        public async Task Buzon_SoloNoLeidas_DejaFueraLasYaLeidas()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(2, USUARIO, new DateTime(2026, 8, 27), leida: new DateTime(2026, 8, 27))
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, true, 1, 20);

            Assert.AreEqual(1, buzon.Count);
            Assert.IsFalse(buzon[0].Leida);
        }

        [TestMethod]
        public async Task Buzon_DevuelveLasMasRecientesPrimero()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 20)),
                EnBuzon(2, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(3, USUARIO, new DateTime(2026, 8, 24))
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 1, 20);

            CollectionAssert.AreEqual(new[] { 2, 3, 1 }, buzon.Select(n => n.Id).ToArray());
        }

        [TestMethod]
        public async Task Buzon_Pagina_DevuelveElTramoQueToca()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 20)),
                EnBuzon(2, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(3, USUARIO, new DateTime(2026, 8, 24))
            }.AsQueryable());

            List<NotificacionBuzonDTO> segunda = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 2, 2);

            Assert.AreEqual(1, segunda.Count);
            Assert.AreEqual(1, segunda[0].Id, "La más antigua cae en la segunda página");
        }

        [TestMethod]
        public async Task Buzon_ContarNoLeidas_CuentaSoloLasDelUsuarioSinLeerNiEliminar()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(2, USUARIO, new DateTime(2026, 8, 27)),
                EnBuzon(3, USUARIO, new DateTime(2026, 8, 27), leida: new DateTime(2026, 8, 27)),
                EnBuzon(4, USUARIO, new DateTime(2026, 8, 27), eliminada: new DateTime(2026, 8, 27)),
                EnBuzon(5, OTRO_USUARIO, new DateTime(2026, 8, 27))
            }.AsQueryable());

            int noLeidas = await servicio.ContarNoLeidas(USUARIO, APLICACION);

            Assert.AreEqual(2, noLeidas);
        }

        [TestMethod]
        public async Task Buzon_MarcarLeida_PoneLaFechaYDevuelveTrue()
        {
            NotificacionBuzon notificacion = EnBuzon(1, USUARIO, new DateTime(2026, 8, 27));
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon> { notificacion }.AsQueryable());

            bool marcada = await servicio.MarcarLeida(1, USUARIO);

            Assert.IsTrue(marcada);
            Assert.IsNotNull(notificacion.FechaLeida);
        }

        [TestMethod]
        public async Task Buzon_MarcarLeida_LaDeOtroUsuario_NoSePuede()
        {
            // Sin el filtro por usuario, cualquiera podría marcar por id las notificaciones ajenas.
            NotificacionBuzon ajena = EnBuzon(1, OTRO_USUARIO, new DateTime(2026, 8, 27));
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon> { ajena }.AsQueryable());

            bool marcada = await servicio.MarcarLeida(1, USUARIO);

            Assert.IsFalse(marcada);
            Assert.IsNull(ajena.FechaLeida);
        }

        [TestMethod]
        public async Task Buzon_MarcarTodasLeidas_SoloTocaLasDelUsuarioSinLeer()
        {
            NotificacionBuzon mia1 = EnBuzon(1, USUARIO, new DateTime(2026, 8, 27));
            NotificacionBuzon mia2 = EnBuzon(2, USUARIO, new DateTime(2026, 8, 27));
            NotificacionBuzon ajena = EnBuzon(3, OTRO_USUARIO, new DateTime(2026, 8, 27));
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon> { mia1, mia2, ajena }.AsQueryable());

            int marcadas = await servicio.MarcarTodasLeidas(USUARIO, APLICACION);

            Assert.AreEqual(2, marcadas);
            Assert.IsNotNull(mia1.FechaLeida);
            Assert.IsNotNull(mia2.FechaLeida);
            Assert.IsNull(ajena.FechaLeida);
        }

        [TestMethod]
        public async Task Buzon_Eliminar_EsBorradoLogico()
        {
            NotificacionBuzon notificacion = EnBuzon(1, USUARIO, new DateTime(2026, 8, 27));
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon> { notificacion }.AsQueryable());

            bool eliminada = await servicio.EliminarDelBuzon(1, USUARIO);

            Assert.IsTrue(eliminada);
            Assert.IsNotNull(notificacion.FechaEliminada);
            A.CallTo(() => fakeBuzon.Remove(A<NotificacionBuzon>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Buzon_Eliminar_LaDeOtroUsuario_NoSePuede()
        {
            NotificacionBuzon ajena = EnBuzon(1, OTRO_USUARIO, new DateTime(2026, 8, 27));
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon> { ajena }.AsQueryable());

            bool eliminada = await servicio.EliminarDelBuzon(1, USUARIO);

            Assert.IsFalse(eliminada);
            Assert.IsNull(ajena.FechaEliminada);
        }

        [TestMethod]
        public async Task Buzon_DevuelveLosDatosDeserializadosParaPoderNavegar()
        {
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27), datos: "{\"tipo\":\"protocolo\",\"videoId\":\"42\"}")
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 1, 20);

            Assert.AreEqual("protocolo", buzon[0].Datos["tipo"]);
            Assert.AreEqual("42", buzon[0].Datos["videoId"]);
        }

        [TestMethod]
        public async Task Buzon_ConDatosCorruptos_DevuelveLaNotificacionSinDatos()
        {
            // Un JSON roto de una fila no puede tumbar el buzón entero.
            ConfigurarFakeDbSet(fakeBuzon, new List<NotificacionBuzon>
            {
                EnBuzon(1, USUARIO, new DateTime(2026, 8, 27), datos: "esto no es json")
            }.AsQueryable());

            List<NotificacionBuzonDTO> buzon = await servicio.ObtenerBuzon(USUARIO, APLICACION, false, 1, 20);

            Assert.AreEqual(1, buzon.Count);
            Assert.IsNull(buzon[0].Datos);
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
    }
}
