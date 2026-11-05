using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#822 (Carlos 05/11/26): el buzón (la campana) es de la PERSONA, no de la aplicación y el usuario. Lo que llega a
    /// «NUEVAVISION\Carlos» en Nesto lo ve «Carlos» en Ariadna y en NestoApp, y al revés; marcar leída o borrar en una se ve
    /// en las otras, y los Nesto abiertos de la persona reciben «hayNotificacionesNuevas» por SignalR.
    /// </summary>
    [TestClass]
    public class BuzonPorPersonaTests
    {
        private const string NESTO = "Nesto";
        private const string ARIADNA = "Ariadna";
        private const string NESTO_APP = "NestoApp";
        private const string DE_DOMINIO = @"NUEVAVISION\Carlos";

        private NVEntities db;
        private DbSet<NotificacionBuzon> fakeBuzon;
        private DbSet<DispositivoNotificacion> fakeDispositivos;
        private List<NotificacionBuzon> buzon;
        private List<DispositivoNotificacion> dispositivos;
        private ServicioNotificacionesPush servicio;
        private List<List<string>> avisados;

        [TestInitialize]
        public void Inicializar()
        {
            var gemelos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [DE_DOMINIO] = DE_DOMINIO
            };
            UsuarioAuditoriaHelper.Configurar(g => gemelos.TryGetValue(g, out string f) ? f : null);

            db = A.Fake<NVEntities>();
            fakeBuzon = A.Fake<DbSet<NotificacionBuzon>>(o => o.Implements<IQueryable<NotificacionBuzon>>().Implements<IDbAsyncEnumerable<NotificacionBuzon>>());
            fakeDispositivos = A.Fake<DbSet<DispositivoNotificacion>>(o => o.Implements<IQueryable<DispositivoNotificacion>>().Implements<IDbAsyncEnumerable<DispositivoNotificacion>>());
            A.CallTo(() => db.NotificacionesBuzon).Returns(fakeBuzon);
            A.CallTo(() => db.DispositivosNotificaciones).Returns(fakeDispositivos);

            buzon = new List<NotificacionBuzon>();
            dispositivos = new List<DispositivoNotificacion>();
            ConfigurarFakeDbSet(fakeBuzon, buzon);
            ConfigurarFakeDbSet(fakeDispositivos, dispositivos);
            int siguienteId = 100;
            A.CallTo(() => fakeBuzon.Add(A<NotificacionBuzon>._))
                .Invokes((NotificacionBuzon n) => { n.Id = siguienteId++; buzon.Add(n); });

            avisados = new List<List<string>>();
            IAvisosTiempoReal avisos = A.Fake<IAvisosTiempoReal>();
            A.CallTo(() => avisos.HayNotificacionesNuevas(A<IEnumerable<string>>._))
                .Invokes((IEnumerable<string> u) => avisados.Add(u.ToList()));
            servicio = new ServicioNotificacionesPush(() => db)
            {
                AvisosTiempoReal = avisos,
                EnviadorDatos = (tokens, datos, aplicacion) => Task.FromResult(tokens.Count)
            };
        }

        [TestCleanup]
        public void Limpiar() => UsuarioAuditoriaHelper.Reiniciar();

        [TestMethod]
        public async Task LoQueLlegaANestoLoVeLaPersonaEnAriadnaYEnNestoApp()
        {
            await servicio.GuardarEnBuzonDeUsuario(DE_DOMINIO, NESTO, new NotificacionPushDTO { Titulo = "Sugerencia de Ariadna" });

            foreach (string aplicacion in new[] { ARIADNA, NESTO_APP })
            {
                List<NotificacionBuzonDTO> suyas = await servicio.ObtenerBuzon("Carlos", aplicacion, false, 1, 20);
                Assert.AreEqual(1, suyas.Count, aplicacion);
                Assert.AreEqual(NESTO, suyas[0].Aplicacion, "El cliente sabe para qué aplicación se grabó (decide si puede abrirlo)");
                Assert.AreEqual(1, await servicio.ContarNoLeidas("Carlos", aplicacion), aplicacion);
            }
        }

        [TestMethod]
        public async Task LoQueLlegaAAriadnaLoVeLaPersonaEnNesto()
        {
            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, new NotificacionPushDTO { Titulo = "Reposición recibida" });

            Assert.AreEqual(1, (await servicio.ObtenerBuzon(DE_DOMINIO, NESTO, false, 1, 20)).Count);
            Assert.AreEqual(1, await servicio.ContarNoLeidas(DE_DOMINIO, NESTO));
        }

        [TestMethod]
        public async Task LoGrabadoAntesPorAplicacionYSinDominioSeVeEnTodas()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = "Carlos", Aplicacion = ARIADNA, Titulo = "Antes de #823", FechaCreacion = new DateTime(2026, 11, 4) });
            buzon.Add(new NotificacionBuzon { Id = 2, Usuario = "Carlos", Aplicacion = NESTO_APP, Titulo = "De NestoApp", FechaCreacion = new DateTime(2026, 11, 4) });
            buzon.Add(new NotificacionBuzon { Id = 3, Usuario = DE_DOMINIO, Aplicacion = NESTO, Titulo = "De Nesto", FechaCreacion = new DateTime(2026, 11, 4) });

            Assert.AreEqual(3, (await servicio.ObtenerBuzon(DE_DOMINIO, NESTO, false, 1, 20)).Count);
            Assert.AreEqual(3, (await servicio.ObtenerBuzon("Carlos", ARIADNA, false, 1, 20)).Count);
            Assert.AreEqual(3, await servicio.MarcarTodasLeidas("Carlos", NESTO_APP), "Marcar todas desde una app las marca en todas");
            Assert.AreEqual(0, await servicio.ContarNoLeidas(DE_DOMINIO, NESTO));
        }

        [TestMethod]
        public async Task MarcarLeidaDesdeAriadnaLaQuitaDeNestoYAvisaAlNesto()
        {
            await servicio.GuardarEnBuzonDeUsuario(DE_DOMINIO, NESTO, new NotificacionPushDTO { Titulo = "x" });
            avisados.Clear();

            Assert.IsTrue(await servicio.MarcarLeida(buzon.Single().Id, "Carlos"));

            Assert.AreEqual(0, await servicio.ContarNoLeidas(DE_DOMINIO, NESTO));
            Assert.AreEqual(1, avisados.Count, "SignalR para que la campana de Nesto se refresque");
            CollectionAssert.Contains(avisados[0], DE_DOMINIO);
        }

        [TestMethod]
        public async Task BorrarDesdeNestoLaQuitaDeAriadnaYAvisa()
        {
            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, new NotificacionPushDTO { Titulo = "x" });
            avisados.Clear();

            Assert.IsTrue(await servicio.EliminarDelBuzon(buzon.Single().Id, DE_DOMINIO));

            Assert.AreEqual(0, (await servicio.ObtenerBuzon("Carlos", ARIADNA, false, 1, 20)).Count);
            Assert.AreEqual(1, avisados.Count);
        }

        [TestMethod]
        public async Task MarcarLeidaYaLeidaNoAvisa()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = DE_DOMINIO, Aplicacion = NESTO, Titulo = "x", FechaCreacion = DateTime.Now, FechaLeida = DateTime.Now });

            Assert.IsTrue(await servicio.MarcarLeida(1, "Carlos"));
            Assert.AreEqual(0, avisados.Count);
        }

        [TestMethod]
        public async Task UnAvisoDeAriadnaAvisaPorSignalRAlNestoDeLaPersona()
        {
            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, new NotificacionPushDTO { Titulo = "x" });

            Assert.AreEqual(1, avisados.Count, "Sale en la misma campana: Nesto tiene que refrescarla");
            CollectionAssert.AreEqual(new[] { DE_DOMINIO }, avisados[0]);
        }

        [TestMethod]
        public async Task ElMismoAvisoParaNestoYAriadnaSeGuardaUnaVez()
        {
            // Como AvisadorReposiciones o SinEtiquetaRecepcion: el mismo aviso a Nesto (con dominio) y a Ariadna (sin dominio).
            var aviso = new NotificacionPushDTO { Titulo = "Sin etiqueta", Cuerpo = "17404", Datos = new Dictionary<string, string> { ["tipo"] = "SinEtiqueta" } };
            await servicio.GuardarEnBuzonDeUsuario(DE_DOMINIO, NESTO, aviso);
            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, aviso);

            Assert.AreEqual(1, buzon.Count, "Una sola fila: si no, la persona lo vería dos veces en su campana");
            Assert.AreEqual(1, await servicio.ContarNoLeidas("Carlos", ARIADNA));
        }

        [TestMethod]
        public async Task AvisosDistintosOElMismoYaBorradoSiSeGuardan()
        {
            await servicio.GuardarEnBuzonDeUsuario(DE_DOMINIO, NESTO, new NotificacionPushDTO { Titulo = "Uno", Cuerpo = "a" });
            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, new NotificacionPushDTO { Titulo = "Uno", Cuerpo = "b" });
            Assert.AreEqual(2, buzon.Count, "Otro cuerpo, otro aviso");

            buzon.ForEach(n => n.FechaEliminada = DateTime.Now);
            await servicio.GuardarEnBuzonDeUsuario(DE_DOMINIO, NESTO, new NotificacionPushDTO { Titulo = "Uno", Cuerpo = "a" });
            Assert.AreEqual(3, buzon.Count, "Si ya lo había borrado, el nuevo se guarda");
        }

        [TestMethod]
        public async Task ElMismoAvisoDeHaceRatoSeVuelveAGuardar()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = DE_DOMINIO, Aplicacion = NESTO, Titulo = "Recordatorio", FechaCreacion = DateTime.Now.AddHours(-1) });

            await servicio.GuardarEnBuzonDeUsuario("Carlos", ARIADNA, new NotificacionPushDTO { Titulo = "Recordatorio" });

            Assert.AreEqual(2, buzon.Count);
        }

        [TestMethod]
        public async Task ElBuzonDeLaTiendaSigueSeparado()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = "Carlos", Aplicacion = "NestoTiendas", Titulo = "De la tienda", FechaCreacion = new DateTime(2026, 11, 4) });
            buzon.Add(new NotificacionBuzon { Id = 2, Usuario = DE_DOMINIO, Aplicacion = NESTO, Titulo = "De Nesto", FechaCreacion = new DateTime(2026, 11, 4) });

            Assert.AreEqual(1, (await servicio.ObtenerBuzon(DE_DOMINIO, NESTO, false, 1, 20)).Count, "Nesto no ve la de la tienda");
            List<NotificacionBuzonDTO> tienda = await servicio.ObtenerBuzon("Carlos", "NestoTiendas", false, 1, 20);
            Assert.AreEqual(1, tienda.Count, "La tienda no ve la de Nesto");
            Assert.AreEqual("De la tienda", tienda[0].Titulo);
        }

        [TestMethod]
        public async Task ElBuzonDeOtraPersonaNoSeVeNiSeMarca()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = "Pedro", Aplicacion = ARIADNA, Titulo = "De Pedro", FechaCreacion = new DateTime(2026, 11, 4) });

            Assert.AreEqual(0, (await servicio.ObtenerBuzon(DE_DOMINIO, NESTO, false, 1, 20)).Count);
            Assert.IsFalse(await servicio.MarcarLeida(1, "Carlos"));
            Assert.IsFalse(await servicio.EliminarDelBuzon(1, DE_DOMINIO));
        }

        [TestMethod]
        public void AplicacionesDelBuzon_LasDeEmpleadosCompartenYLaTiendaNo()
        {
            CollectionAssert.AreEquivalent(new[] { NESTO, ARIADNA, NESTO_APP }, ServicioNotificacionesPush.AplicacionesDelBuzon(ARIADNA));
            CollectionAssert.AreEquivalent(new[] { NESTO, ARIADNA, NESTO_APP }, ServicioNotificacionesPush.AplicacionesDelBuzon("nesto"));
            CollectionAssert.AreEqual(new[] { "NestoTiendas" }, ServicioNotificacionesPush.AplicacionesDelBuzon("NestoTiendas"));
        }

        [TestMethod]
        public void GruposDe_LaConexionDeAriadnaEntraTambienEnElGrupoDeLaPersona()
        {
            CollectionAssert.AreEqual(new[] { "usuario:CARLOS", @"usuario:NUEVAVISION\CARLOS" }, AvisosTiempoReal.GruposDe("Carlos"));
            CollectionAssert.AreEqual(new[] { @"usuario:NUEVAVISION\CARLOS" }, AvisosTiempoReal.GruposDe(DE_DOMINIO), "Nesto: uno solo");
            CollectionAssert.AreEqual(new[] { "usuario:PEDRO" }, AvisosTiempoReal.GruposDe("Pedro"), "Sin gemelo: el suyo");
            Assert.AreEqual(0, AvisosTiempoReal.GruposDe("  ").Count);
        }

        [TestMethod]
        public void HayNotificacionesNuevas_VaAlNestoYALaAriadnaDeLaPersona()
        {
            var nesto = new HubDePrueba();
            var almacen = new HubDePrueba();
            var tiempoReal = new AvisosTiempoReal(() => nesto.Contexto, () => almacen.Contexto);

            tiempoReal.HayNotificacionesNuevas(new[] { DE_DOMINIO, "Carlos" });

            CollectionAssert.AreEquivalent(new[] { @"usuario:NUEVAVISION\CARLOS", "usuario:CARLOS" }, nesto.Avisados);
            CollectionAssert.AreEquivalent(new[] { @"usuario:NUEVAVISION\CARLOS", "usuario:CARLOS" }, almacen.Avisados,
                "Ariadna (AlmacenHub) también: refresca la campana sin reloj (Ariadna#97)");
        }

        [TestMethod]
        public void HayNotificacionUrgente_SoloAlNesto()
        {
            var nesto = new HubDePrueba();
            var almacen = new HubDePrueba();
            var tiempoReal = new AvisosTiempoReal(() => nesto.Contexto, () => almacen.Contexto);

            tiempoReal.HayNotificacionUrgente(new[] { DE_DOMINIO });

            Assert.AreEqual(1, nesto.Urgentes.Count);
            Assert.AreEqual(0, almacen.Urgentes.Count + almacen.Avisados.Count, "La ventana roja es de Nesto");
        }

        [TestMethod]
        public void HayNotificacionesNuevas_SiFallaElHubDeAlmacenElDeNestoSaleIgual()
        {
            var nesto = new HubDePrueba();
            var tiempoReal = new AvisosTiempoReal(() => nesto.Contexto, () => throw new InvalidOperationException("sin SignalR"));

            tiempoReal.HayNotificacionesNuevas(new[] { DE_DOMINIO });

            Assert.AreEqual(1, nesto.Avisados.Count);
        }

        /// <summary>Un hub de SignalR de mentira: apunta a qué grupo se manda cada evento.</summary>
        public sealed class HubDePrueba
        {
            public List<string> Avisados { get; } = new List<string>();
            public List<string> Urgentes { get; } = new List<string>();
            public Microsoft.AspNet.SignalR.IHubContext Contexto { get; }

            public HubDePrueba()
            {
                Contexto = A.Fake<Microsoft.AspNet.SignalR.IHubContext>();
                var clientes = A.Fake<Microsoft.AspNet.SignalR.Hubs.IHubConnectionContext<dynamic>>();
                A.CallTo(() => Contexto.Clients).Returns(clientes);
                A.CallTo(() => clientes.Group(A<string>._, A<string[]>._))
                    .ReturnsLazily((string grupo, string[] excluidas) => new GrupoDePrueba(this, grupo));
            }

            public sealed class GrupoDePrueba
            {
                private readonly HubDePrueba hub;
                private readonly string grupo;
                public GrupoDePrueba(HubDePrueba hub, string grupo) { this.hub = hub; this.grupo = grupo; }
                public void hayNotificacionesNuevas() => hub.Avisados.Add(grupo);
                public void hayNotificacionUrgente() => hub.Urgentes.Add(grupo);
            }
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(lista.AsQueryable().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(lista.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).ReturnsLazily(() => lista.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => lista.GetEnumerator());
        }
    }
}
