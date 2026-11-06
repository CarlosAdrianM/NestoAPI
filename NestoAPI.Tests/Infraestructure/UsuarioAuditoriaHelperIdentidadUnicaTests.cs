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
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#823 (Carlos 05/11/26): una persona, una identidad. «Santiago» (Ariadna/NestoApp, /oauth/token) es
    /// «NUEVAVISION\santiago» (Nesto) si existe esa fila en FechasPermitidas o en ParámetrosUsuario: los SPs (FechasPermitidas),
    /// los triggers y las trazas ven a una sola persona. Caso real: «Fecha no permitida» al facturar al imprimir la etiqueta
    /// del pedido 920033 desde Ariadna (ELMAH 05/11/26 11:33).
    /// </summary>
    [TestClass]
    public class UsuarioAuditoriaHelperIdentidadUnicaTests
    {
        /// <summary>Lo que hay en la BD de mentira: el gemelo, con la caja de su fila.</summary>
        private Dictionary<string, string> filas;
        private List<string> consultas;
        private List<Exception> registrados;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            filas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"NUEVAVISION\Santiago"] = @"NUEVAVISION\santiago",
                [@"NUEVAVISION\Alfredo"] = @"NUEVAVISION\Alfredo",
                [@"NUEVAVISION\Reina"] = @"NUEVAVISION\REINA"
            };
            consultas = new List<string>();
            registrados = new List<Exception>();
            ahora = new DateTime(2026, 11, 5, 12, 0, 0);
            UsuarioAuditoriaHelper.Configurar(Buscar, registrados.Add, () => ahora);
        }

        [TestCleanup]
        public void Limpiar()
        {
            UsuarioAuditoriaHelper.Reiniciar();
        }

        private string Buscar(string gemelo)
        {
            consultas.Add(gemelo);
            return filas.TryGetValue(gemelo, out string fila) ? fila : null;
        }

        private static IPrincipal Token(string nombre) => new GenericPrincipal(new GenericIdentity(nombre), new string[0]);

        // ---- Persona / Resolver / ParaAuditoria ----

        [TestMethod]
        public void SinDominioConGemelo_DevuelveElDeDominioConLaCajaDeSuFila()
        {
            Assert.AreEqual(@"NUEVAVISION\santiago", UsuarioAuditoriaHelper.Persona("Santiago"),
                "La caja es la de la fila: los triggers y el histórico lo graban así");
            Assert.AreEqual(@"NUEVAVISION\REINA", UsuarioAuditoriaHelper.Persona("Reina"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Santiago", @"NUEVAVISION\Reina" }, consultas,
                "Se pregunta por NUEVAVISION\\<nombre>");
        }

        [TestMethod]
        public void SinDominioSinGemelo_SeQuedaComoEstaba()
        {
            Assert.AreEqual("Pedro", UsuarioAuditoriaHelper.Persona("Pedro"), "Pedro no tiene usuario de dominio");
            Assert.AreEqual("RevisionGoogle", UsuarioAuditoriaHelper.Persona("RevisionGoogle"));
        }

        [TestMethod]
        public void ConDominioOCorreoOVacio_NiSeConsulta()
        {
            Assert.AreEqual(@"NUEVAVISION\Alfredo", UsuarioAuditoriaHelper.Persona(@"NUEVAVISION\Alfredo"));
            Assert.AreEqual("cliente@ejemplo.com", UsuarioAuditoriaHelper.Persona("cliente@ejemplo.com"));
            Assert.IsNull(UsuarioAuditoriaHelper.Persona(null));
            Assert.AreEqual("", UsuarioAuditoriaHelper.Persona(""));
            Assert.AreEqual(0, consultas.Count);
        }

        [TestMethod]
        public void Resolver_TokenSinDominio_DevuelveElGemelo()
        {
            Assert.AreEqual(@"NUEVAVISION\santiago", UsuarioAuditoriaHelper.Resolver(Token("Santiago"), "NestoAPI"));
            Assert.AreEqual(@"NUEVAVISION\Alfredo", UsuarioAuditoriaHelper.Resolver(Token(@"NUEVAVISION\Alfredo"), "NestoAPI"));
            Assert.AreEqual("Pedro", UsuarioAuditoriaHelper.Resolver(Token("Pedro"), "NestoAPI"));
        }

        [TestMethod]
        public void Resolver_SinToken_ElFallbackTambienSeResuelve()
        {
            Assert.AreEqual("NestoAPI", UsuarioAuditoriaHelper.Resolver(null, "NestoAPI"));
            Assert.AreEqual(@"NUEVAVISION\Alfredo", UsuarioAuditoriaHelper.Resolver(null, "Alfredo"));
            Assert.IsNull(UsuarioAuditoriaHelper.Resolver(null, null));
        }

        [TestMethod]
        public void ParaAuditoria_ResuelveRecortaYNuncaVacio()
        {
            Assert.AreEqual(@"NUEVAVISION\santiago", UsuarioAuditoriaHelper.ParaAuditoria(" Santiago "));
            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, UsuarioAuditoriaHelper.ParaAuditoria("  "));
            Assert.AreEqual(30, UsuarioAuditoriaHelper.ParaAuditoria(new string('x', 40) + @"\y").Length);
        }

        [TestMethod]
        public void SinConsultor_NoSeResuelveNada()
        {
            UsuarioAuditoriaHelper.Reiniciar();

            Assert.AreEqual("Santiago", UsuarioAuditoriaHelper.Persona("Santiago"));
            Assert.AreEqual("Santiago", UsuarioAuditoriaHelper.ParaAuditoria("Santiago"));
        }

        // ---- Caché ----

        [TestMethod]
        public void Cache_UnaConsultaPorNombreCadaDiezMinutos_SinDistinguirMayusculas()
        {
            _ = UsuarioAuditoriaHelper.Persona("Santiago");
            _ = UsuarioAuditoriaHelper.Persona("santiago");
            _ = UsuarioAuditoriaHelper.Persona("Pedro");
            _ = UsuarioAuditoriaHelper.Persona("Pedro");
            Assert.AreEqual(2, consultas.Count, "También se guarda que Pedro no tiene gemelo");

            ahora = ahora.AddMinutes(9);
            _ = UsuarioAuditoriaHelper.Persona("Santiago");
            Assert.AreEqual(2, consultas.Count);

            ahora = ahora.AddMinutes(2);
            filas[@"NUEVAVISION\Pedro"] = @"NUEVAVISION\Pedro";
            Assert.AreEqual(@"NUEVAVISION\Pedro", UsuarioAuditoriaHelper.Persona("Pedro"), "Al caducar se vuelve a mirar");
            Assert.AreEqual(3, consultas.Count);
        }

        [TestMethod]
        public void Invalidar_LaSiguienteVuelveAConsultar()
        {
            Assert.AreEqual("Pedro", UsuarioAuditoriaHelper.Persona("Pedro"));
            filas[@"NUEVAVISION\Pedro"] = @"NUEVAVISION\Pedro";

            UsuarioAuditoriaHelper.Invalidar();

            Assert.AreEqual(@"NUEVAVISION\Pedro", UsuarioAuditoriaHelper.Persona("Pedro"));
        }

        // ---- Fallo de la BD: nunca rompe una petición ----

        [TestMethod]
        public void FalloDeLaBD_DevuelveElNombreTalCualYAvisaUnaSolaVez()
        {
            UsuarioAuditoriaHelper.Configurar(_ => throw new InvalidOperationException("sin conexión"), registrados.Add, () => ahora);

            Assert.AreEqual("Santiago", UsuarioAuditoriaHelper.Persona("Santiago"));
            Assert.AreEqual("Alfredo", UsuarioAuditoriaHelper.ParaAuditoria("Alfredo"));
            Assert.AreEqual("Andre", UsuarioAuditoriaHelper.Resolver(Token("Andre"), null));

            Assert.AreEqual(1, registrados.Count, "ELMAH una vez, no en cada petición");
            StringAssert.Contains(registrados[0].Message, "NestoAPI#823");
        }

        [TestMethod]
        public void FalloAlRegistrar_TampocoRompe()
        {
            UsuarioAuditoriaHelper.Configurar(_ => throw new InvalidOperationException("sin conexión"), _ => throw new Exception("sin ELMAH"), () => ahora);

            Assert.AreEqual("Santiago", UsuarioAuditoriaHelper.Persona("Santiago"));
        }

        // ---- Nombres de la persona (búsquedas que deben ver lo grabado antes de #823) ----

        [TestMethod]
        public void NombresDeLaPersona_ConGemelo_ElDeDominioYElSinDominio()
        {
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Alfredo", "Alfredo" }, UsuarioAuditoriaHelper.NombresDeLaPersona("Alfredo"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Alfredo", "Alfredo" }, UsuarioAuditoriaHelper.NombresDeLaPersona(@"NUEVAVISION\Alfredo"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\santiago", "santiago", "Santiago" }, UsuarioAuditoriaHelper.NombresDeLaPersona("Santiago"),
                "Con la caja de la fila y con la que se pasó (en SQL da igual; en memoria no)");
        }

        [TestMethod]
        public void NombresDeLaPersona_SinGemelo_SoloElSuyo()
        {
            CollectionAssert.AreEqual(new[] { "Pedro" }, UsuarioAuditoriaHelper.NombresDeLaPersona("Pedro"));
            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Pepe" }, UsuarioAuditoriaHelper.NombresDeLaPersona(@"NUEVAVISION\Pepe"),
                "Sin fila de NUEVAVISION\\Pepe, «Pepe» no se da por la misma persona");
            Assert.AreEqual(0, UsuarioAuditoriaHelper.NombresDeLaPersona(null).Count);
        }
    }

    /// <summary>
    /// NestoAPI#823: el buzón de Ariadna (y de NestoApp) con la persona resuelta. Lo que se escribe y lo que se lee usan el
    /// MISMO nombre (el de dominio) y lo grabado antes con el nombre sin dominio se sigue viendo; las push siguen llegando a
    /// los dispositivos, que están registrados con el usuario de Identity («Alfredo»).
    /// </summary>
    [TestClass]
    public class BuzonIdentidadUnicaTests
    {
        private const string ARIADNA = "Ariadna";
        private const string TOKEN_PDA = "token-de-la-pda";

        private NVEntities db;
        private DbSet<NotificacionBuzon> fakeBuzon;
        private DbSet<DispositivoNotificacion> fakeDispositivos;
        private List<NotificacionBuzon> buzon;
        private List<DispositivoNotificacion> dispositivos;
        private ServicioNotificacionesPush servicio;
        private List<(List<string> Tokens, Dictionary<string, string> Datos)> pushes;

        [TestInitialize]
        public void Inicializar()
        {
            var gemelos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [@"NUEVAVISION\Alfredo"] = @"NUEVAVISION\Alfredo"
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

            servicio = new ServicioNotificacionesPush(() => db)
            {
                AvisosTiempoReal = A.Fake<IAvisosTiempoReal>()
            };
            pushes = new List<(List<string>, Dictionary<string, string>)>();
            servicio.EnviadorDatos = (tokens, datos, aplicacion) =>
            {
                pushes.Add((tokens.ToList(), datos));
                return Task.FromResult(tokens.Count);
            };
        }

        [TestCleanup]
        public void Limpiar() => UsuarioAuditoriaHelper.Reiniciar();

        [TestMethod]
        public async Task Ariadna_CreaYLeeConElMismoNombreResuelto()
        {
            // Como AvisadorReposiciones: escribe a Ariadna con el nombre sin dominio.
            await servicio.GuardarEnBuzonDeUsuario("Alfredo", ARIADNA, new NotificacionPushDTO { Titulo = "Reposición recibida" });

            Assert.AreEqual(@"NUEVAVISION\Alfredo", buzon.Single().Usuario, "Se graba a nombre de la persona");
            Assert.AreEqual(ARIADNA, buzon.Single().Aplicacion, "La aplicación se queda");

            // Ariadna pide su buzón: el controlador pasa la persona resuelta (y si pasara «Alfredo» daría lo mismo).
            foreach (string comoPide in new[] { @"NUEVAVISION\Alfredo", "Alfredo" })
            {
                List<NotificacionBuzonDTO> suyas = await servicio.ObtenerBuzon(comoPide, ARIADNA, false, 1, 20);
                Assert.AreEqual(1, suyas.Count, comoPide);
                Assert.AreEqual(1, await servicio.ContarNoLeidas(comoPide, ARIADNA), comoPide);
            }

            Assert.IsTrue(await servicio.MarcarLeida(buzon.Single().Id, @"NUEVAVISION\Alfredo"));
            Assert.AreEqual(0, await servicio.ContarNoLeidas("Alfredo", ARIADNA));
            Assert.IsTrue(await servicio.EliminarDelBuzon(buzon.Single().Id, "Alfredo"));
            Assert.AreEqual(0, (await servicio.ObtenerBuzon(@"NUEVAVISION\Alfredo", ARIADNA, false, 1, 20)).Count);
        }

        [TestMethod]
        public async Task Ariadna_LoGrabadoAntesSinDominioSeSigueViendo()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = "Alfredo", Aplicacion = ARIADNA, Titulo = "De antes de #823", FechaCreacion = new DateTime(2026, 11, 4) });

            List<NotificacionBuzonDTO> suyas = await servicio.ObtenerBuzon(@"NUEVAVISION\Alfredo", ARIADNA, false, 1, 20);

            Assert.AreEqual(1, suyas.Count);
            Assert.AreEqual(1, await servicio.MarcarTodasLeidas(@"NUEVAVISION\Alfredo", ARIADNA));
        }

        [TestMethod]
        public async Task Ariadna_ElBuzonDeOtraPersonaNoSeVe()
        {
            buzon.Add(new NotificacionBuzon { Id = 1, Usuario = "Pedro", Aplicacion = ARIADNA, Titulo = "De Pedro", FechaCreacion = new DateTime(2026, 11, 4) });
            buzon.Add(new NotificacionBuzon { Id = 2, Usuario = @"NUEVAVISION\Alfredo", Aplicacion = "Nesto", Titulo = "De su Nesto", FechaCreacion = new DateTime(2026, 11, 4) });

            Assert.AreEqual(1, (await servicio.ObtenerBuzon("Alfredo", ARIADNA, false, 1, 20)).Count,
                "La de Pedro no; la de su Nesto sí (NestoAPI#822: un buzón por persona)");
            Assert.IsFalse(await servicio.MarcarLeida(1, "Alfredo"));
        }

        [TestMethod]
        public async Task Ariadna_LaPushLlegaALosDispositivosRegistradosConElUsuarioDeIdentity()
        {
            dispositivos.Add(new DispositivoNotificacion { Usuario = "Alfredo", Token = TOKEN_PDA, Aplicacion = ARIADNA, Activo = true });

            // Quien avisa ya pasa la persona resuelta.
            await servicio.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Alfredo", ARIADNA, new NotificacionPushDTO { Titulo = "Te han contestado" });

            Assert.AreEqual(1, pushes.Count, "El dispositivo está a nombre de «Alfredo»: hay que encontrarlo igual");
            CollectionAssert.AreEqual(new[] { TOKEN_PDA }, pushes[0].Tokens);
            Assert.AreEqual("Alfredo", pushes[0].Datos["usuarioDestino"],
                "La PDA compara con el mozo que está dentro (usuario de Identity, sin dominio)");
        }

        [TestMethod]
        public async Task Nesto_AvisaPorSignalRAlaPersonaResuelta()
        {
            await servicio.GuardarEnBuzonDeUsuario("Alfredo", "Nesto", new NotificacionPushDTO { Titulo = "x" });

            A.CallTo(() => servicio.AvisosTiempoReal.HayNotificacionesNuevas(A<IEnumerable<string>>.That.Matches(u => u.Single() == @"NUEVAVISION\Alfredo")))
                .MustHaveHappenedOnceExactly();
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
