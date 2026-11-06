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

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#833 (Carlos 05/11/26): de los avisos que nacen para Nesto y se guardan en el buzón de una persona, llegan además como
    /// push a su PDA (Ariadna) SOLO los urgentes y los de las personas de Dirección o Informática. Sin segunda fila en el buzón.
    /// Y GET api/Notificaciones/Urgentes lee el buzón de la persona (con y sin dominio, cualquier aplicación de los empleados).
    /// </summary>
    [TestClass]
    public class PushAriadnaDesdeNestoTests
    {
        private const string NESTO = "Nesto";
        private const string ARIADNA = "Ariadna";
        private const string CARLOS = @"NUEVAVISION\Carlos";
        private const string PEDRO = @"NUEVAVISION\Pedro";
        private const string ANDRE = @"NUEVAVISION\Andre";

        private NVEntities db;
        private List<NotificacionBuzon> buzon;
        private List<DispositivoNotificacion> dispositivos;
        private ServicioNotificacionesPush servicio;
        private List<(List<string> Tokens, Dictionary<string, string> Datos, string Aplicacion)> pushes;
        private HashSet<string> deDireccionOInformatica;
        private Func<string, List<string>> lectorOriginal;

        [TestInitialize]
        public void Inicializar()
        {
            var gemelos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CARLOS] = CARLOS,
                [PEDRO] = PEDRO,
                [ANDRE] = ANDRE
            };
            UsuarioAuditoriaHelper.Configurar(g => gemelos.TryGetValue(g, out string f) ? f : null);
            lectorOriginal = ServicioNotificacionesPush.LectorMiembrosGrupo;

            db = A.Fake<NVEntities>();
            DbSet<NotificacionBuzon> fakeBuzon = A.Fake<DbSet<NotificacionBuzon>>(o => o.Implements<IQueryable<NotificacionBuzon>>().Implements<IDbAsyncEnumerable<NotificacionBuzon>>());
            DbSet<DispositivoNotificacion> fakeDispositivos = A.Fake<DbSet<DispositivoNotificacion>>(o => o.Implements<IQueryable<DispositivoNotificacion>>().Implements<IDbAsyncEnumerable<DispositivoNotificacion>>());
            A.CallTo(() => db.NotificacionesBuzon).Returns(fakeBuzon);
            A.CallTo(() => db.DispositivosNotificaciones).Returns(fakeDispositivos);
            buzon = new List<NotificacionBuzon>();
            dispositivos = new List<DispositivoNotificacion>();
            ConfigurarFakeDbSet(fakeBuzon, buzon);
            ConfigurarFakeDbSet(fakeDispositivos, dispositivos);
            int siguienteId = 100;
            A.CallTo(() => fakeBuzon.Add(A<NotificacionBuzon>._))
                .Invokes((NotificacionBuzon n) => { n.Id = siguienteId++; buzon.Add(n); });

            // Las PDA de cada uno (Ariadna registra con el usuario de Identity, sin dominio).
            dispositivos.Add(Pda("Carlos", "token-carlos"));
            dispositivos.Add(Pda("Pedro", "token-pedro"));
            dispositivos.Add(Pda("Andre", "token-andre"));

            deDireccionOInformatica = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            pushes = new List<(List<string>, Dictionary<string, string>, string)>();
            servicio = new ServicioNotificacionesPush(() => db)
            {
                AvisosTiempoReal = A.Fake<IAvisosTiempoReal>(),
                EsDeDireccionOInformatica = u => deDireccionOInformatica.Contains(UsuarioAuditoriaHelper.SinDominio(u)),
                EnviadorDatos = (tokens, datos, aplicacion) =>
                {
                    pushes.Add((tokens.ToList(), datos, aplicacion));
                    return Task.FromResult(tokens.Count);
                }
            };
        }

        [TestCleanup]
        public void Limpiar()
        {
            ServicioNotificacionesPush.LectorMiembrosGrupo = lectorOriginal;
            ServicioNotificacionesPush.InvalidarCacheGrupos();
            UsuarioAuditoriaHelper.Reiniciar();
        }

        private static DispositivoNotificacion Pda(string usuario, string token) => new DispositivoNotificacion
        {
            Usuario = usuario,
            Token = token,
            Aplicacion = ARIADNA,
            Plataforma = "Android",
            Activo = true
        };

        private static NotificacionPushDTO Urgente() => new NotificacionPushDTO
        {
            Titulo = "Autorizar reembolso",
            Cuerpo = "45,30 €",
            Datos = new Dictionary<string, string> { ["tipo"] = "AutorizacionReembolsoReparto", ["referencia"] = "7" }
        };

        private static NotificacionPushDTO Normal() => new NotificacionPushDTO
        {
            Titulo = "Reposición recibida",
            Cuerpo = "R123",
            Datos = new Dictionary<string, string> { ["tipo"] = "ReposicionRecibida" }
        };

        [TestMethod]
        public async Task Urgente_VaTambienALaPdaDeLaPersona()
        {
            await servicio.GuardarEnBuzonDeUsuario(ANDRE, NESTO, Urgente());

            Assert.AreEqual(1, pushes.Count);
            CollectionAssert.AreEqual(new[] { "token-andre" }, pushes[0].Tokens);
            Assert.AreEqual(ARIADNA, pushes[0].Aplicacion);
            Assert.AreEqual("Andre", pushes[0].Datos["usuarioDestino"], "La PDA compara con el mozo de dentro, sin dominio");
            Assert.AreEqual("AutorizacionReembolsoReparto", pushes[0].Datos["tipo"]);
            Assert.AreEqual(1, buzon.Count, "Una sola fila en el buzón");
            Assert.AreEqual(NESTO, buzon[0].Aplicacion);
        }

        [TestMethod]
        public async Task AvisoADireccion_VaTambienALaPda()
        {
            deDireccionOInformatica.Add("Pedro");

            await servicio.GuardarEnBuzonDeUsuario(PEDRO, NESTO, Normal());

            Assert.AreEqual(1, pushes.Count);
            CollectionAssert.AreEqual(new[] { "token-pedro" }, pushes[0].Tokens);
            Assert.AreEqual(1, buzon.Count);
        }

        [TestMethod]
        public async Task AvisoAInformatica_VaTambienALaPda_PorElGrupoDelDominio()
        {
            // El de producción: lee los grupos del dominio (aquí, de mentira).
            ServicioNotificacionesPush.InvalidarCacheGrupos();
            var leidos = new List<string>();
            ServicioNotificacionesPush.LectorMiembrosGrupo = grupo =>
            {
                leidos.Add(grupo);
                return grupo == "Informática" ? new List<string> { "Carlos" } : new List<string> { "Laura" };
            };
            servicio.EsDeDireccionOInformatica = ServicioNotificacionesPush.PersonaDeDireccionOInformatica;

            await servicio.GuardarEnBuzonDeUsuario(CARLOS, NESTO, Normal());
            await servicio.GuardarEnBuzonDeUsuario(ANDRE, NESTO, new NotificacionPushDTO { Titulo = "Otro", Cuerpo = "x" });

            Assert.AreEqual(1, pushes.Count, "Carlos (Informática) sí; Andre no");
            CollectionAssert.AreEqual(new[] { "token-carlos" }, pushes[0].Tokens);
            CollectionAssert.AreEquivalent(new[] { "Dirección", "Informática" }, leidos, "Se leen una vez y quedan en caché");
        }

        [TestMethod]
        public async Task AvisoNormalAUnMozo_NoVaALaPda()
        {
            await servicio.GuardarEnBuzonDeUsuario(ANDRE, NESTO, Normal());

            Assert.AreEqual(0, pushes.Count, "Los mozos no reciben el ruido de Nesto");
            Assert.AreEqual(1, buzon.Count, "Pero sí lo tienen en la campana");
        }

        [TestMethod]
        public async Task SinPda_NiSePreguntaPorElGrupo()
        {
            dispositivos.Clear();
            bool preguntado = false;
            servicio.EsDeDireccionOInformatica = u => preguntado = true;

            await servicio.GuardarEnBuzonDeUsuario(PEDRO, NESTO, Normal());

            Assert.IsFalse(preguntado, "No se va al dominio por quien no tiene PDA");
            Assert.AreEqual(0, pushes.Count);
        }

        [TestMethod]
        public async Task FalloDeLaPush_ElAvisoDeNestoSeGuardaIgual()
        {
            servicio.EnviadorDatos = (tokens, datos, aplicacion) => throw new InvalidOperationException("Firebase caído");

            await servicio.GuardarEnBuzonDeUsuario(ANDRE, NESTO, Urgente());

            Assert.AreEqual(1, buzon.Count);
            Assert.AreEqual(NESTO, buzon[0].Aplicacion);
        }

        [TestMethod]
        public async Task FalloAlMirarElGrupo_ElAvisoDeNestoSeGuardaIgual()
        {
            servicio.EsDeDireccionOInformatica = u => throw new InvalidOperationException("Sin dominio");

            await servicio.GuardarEnBuzonDeUsuario(PEDRO, NESTO, Normal());

            Assert.AreEqual(1, buzon.Count);
            Assert.AreEqual(0, pushes.Count);
        }

        [TestMethod]
        public async Task ElMismoAvisoParaNestoYAriadna_LaPdaSuenaUnaVez()
        {
            // Como AvisadorReposiciones: el mismo aviso a Nesto (con dominio) y a Ariadna (sin dominio), y Pedro es de Dirección.
            deDireccionOInformatica.Add("Pedro");

            await servicio.GuardarEnBuzonDeUsuario(PEDRO, NESTO, Normal());
            await servicio.GuardarEnBuzonDeUsuario("Pedro", ARIADNA, Normal());

            Assert.AreEqual(1, pushes.Count);
            Assert.AreEqual(1, buzon.Count);
        }

        [TestMethod]
        public async Task AvisoDeAriadna_SigueYendoALaPdaComoSiempre()
        {
            await servicio.GuardarEnBuzonDeUsuario("Andre", ARIADNA, Normal());

            Assert.AreEqual(1, pushes.Count, "#575: lo que nace para Ariadna, push siempre");
        }

        // ================================================================ GET api/Notificaciones/Urgentes

        [TestMethod]
        public async Task Urgentes_LeeElBuzonDeLaPersonaConYSinDominioYDeCualquierAplicacion()
        {
            DateTime ahora = DateTime.Now;
            buzon.Add(Fila(1, CARLOS, NESTO, "7", ahora.AddMinutes(-5)));
            buzon.Add(Fila(2, "Carlos", ARIADNA, "8", ahora.AddMinutes(-4)));
            buzon.Add(Fila(3, "Carlos", "NestoTiendas", "9", ahora.AddMinutes(-3)));
            buzon.Add(Fila(4, PEDRO, NESTO, "10", ahora.AddMinutes(-2)));

            var repositorio = new RepositorioUrgentesBuzon(db);
            foreach (string comoPide in new[] { CARLOS, "Carlos" })
            {
                List<NotificacionBuzonDTO> filas = await repositorio.Leer(comoPide, ahora.AddHours(-24));
                CollectionAssert.AreEquivalent(new[] { 1, 2 }, filas.Select(f => f.Id).ToList(), comoPide);
            }
        }

        private static NotificacionBuzon Fila(int id, string usuario, string aplicacion, string referencia, DateTime fecha) => new NotificacionBuzon
        {
            Id = id,
            Usuario = usuario,
            Aplicacion = aplicacion,
            Titulo = "Autorizar reembolso",
            Datos = "{\"tipo\":\"AutorizacionReembolsoReparto\",\"referencia\":\"" + referencia + "\"}",
            FechaCreacion = fecha
        };

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
