using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#673 (Carlos, 19/10/26): aviso en la campana de Nesto al almacén que recibe una reposición que lleva más de 2 días
    // recibida en parte, una vez al día (job diario) mientras siga a medias.
    [TestClass]
    public class AvisoReposicionesRecibidasEnParteTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 10, 19, 9, 15, 0);

        private IServicioRecepcionReposiciones reposiciones;
        private IServicioNotificacionesPush notificaciones;
        private List<UsuarioAlmacen> usuarios;
        private AvisoReposicionesRecibidasEnParte aviso;

        [TestInitialize]
        public void Inicializar()
        {
            reposiciones = A.Fake<IServicioRecepcionReposiciones>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            usuarios = new List<UsuarioAlmacen>
            {
                new UsuarioAlmacen { Usuario = "Paloma", Almacen = "ALC" },
                new UsuarioAlmacen { Usuario = "Carmen ", Almacen = "alc " },
                new UsuarioAlmacen { Usuario = "(defecto)", Almacen = "ALC" },
                new UsuarioAlmacen { Usuario = "Reina", Almacen = "REI" }
            };
            A.CallTo(() => reposiciones.LeerPendientes(A<string>._, A<string>._)).Returns(new List<ReposicionPendienteDTO>());
            aviso = new AvisoReposicionesRecibidasEnParte(reposiciones, notificaciones, _ => Task.FromResult(usuarios))
            {
                Ahora = () => Ahora
            };
        }

        private static ReposicionPendienteDTO AMedias(int traspaso, DateTime? primeraParcial, int lineas = 4, int unidades = 12,
            int lineasRecibidas = 2, int unidadesRecibidas = 5)
        {
            return new ReposicionPendienteDTO
            {
                Traspaso = traspaso,
                Origen = "ALG",
                NombreOrigen = "Algete",
                Lineas = lineas,
                Unidades = unidades,
                LineasRecibidas = lineasRecibidas,
                UnidadesRecibidas = unidadesRecibidas,
                FechaPrimeraParcial = primeraParcial
            };
        }

        [TestMethod]
        public async Task Avisar_MasDeDosDiasAMedias_AvisaEnLaCampanaDeNestoALosUsuariosDeEseAlmacen()
        {
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>
            {
                AMedias(81029, Ahora.AddDays(-3).AddHours(-1))
            });
            var guardadas = new List<(string Usuario, NotificacionPushDTO Notificacion)>();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string _, NotificacionPushDTO n) => guardadas.Add((u, n)));

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            const string texto = "La reposición 81029 desde Algete lleva 3 días recibida en parte: quedan 4 líneas (12 ud.) por dar de alta.";
            Assert.AreEqual(1, avisados.Count);
            Assert.AreEqual(texto, avisados[0].Texto);
            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Paloma", "NUEVAVISION\\Carmen" }, guardadas.Select(g => g.Usuario).ToList());
            Assert.IsTrue(guardadas.All(g => g.Notificacion.Cuerpo == texto && g.Notificacion.Tipo == AvisoReposicionesRecibidasEnParte.TIPO_NOTIFICACION));
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedTwiceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>.That.Contains("(defecto)"), A<string>._, A<NotificacionPushDTO>._))
                .MustNotHaveHappened();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Reina", A<string>._, A<NotificacionPushDTO>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_MenosDeDosDias_NoAvisa()
        {
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>
            {
                AMedias(81029, Ahora.AddDays(-2).AddMinutes(1)),
                AMedias(81030, Ahora.AddHours(-5))
            });

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            Assert.AreEqual(0, avisados.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_PendienteSinNingunaParcial_NoAvisa()
        {
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>
            {
                AMedias(81029, null, lineasRecibidas: 0, unidadesRecibidas: 0),
                AMedias(81031, Ahora.AddDays(-5), lineasRecibidas: 0, unidadesRecibidas: 0)
            });

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            Assert.AreEqual(0, avisados.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_CadaReposicionAlAlmacenQueLaRecibe()
        {
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO> { AMedias(81029, Ahora.AddDays(-4)) });
            A.CallTo(() => reposiciones.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                AMedias(81040, Ahora.AddDays(-3), lineas: 1, unidades: 2)
            });

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            Assert.AreEqual(2, avisados.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Reina", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo ==
                    "La reposición 81040 desde Algete lleva 3 días recibida en parte: quedan 1 línea (2 ud.) por dar de alta.")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Reina", A<string>._,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("81029")))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisar_SiFallaUnAlmacen_SigueConLosDemasYNoLanza()
        {
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALC")).Throws(new InvalidOperationException("BD caída"));
            A.CallTo(() => reposiciones.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO> { AMedias(81040, Ahora.AddDays(-3)) });

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            Assert.AreEqual(81040, avisados.Single().Traspaso);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Reina", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Avisar_SiNoSeLeenLosUsuarios_NoLanza()
        {
            aviso = new AvisoReposicionesRecibidasEnParte(reposiciones, notificaciones, _ => throw new InvalidOperationException("BD caída"));

            List<AvisoRecibidaEnParte> avisados = await aviso.Avisar("1");

            Assert.AreEqual(0, avisados.Count);
        }

        [TestMethod]
        public void Texto_SinNombreDelOrigen_ConElCodigo()
        {
            ReposicionPendienteDTO reposicion = AMedias(81029, Ahora.AddDays(-3));
            reposicion.NombreOrigen = null;

            Assert.AreEqual("La reposición 81029 desde ALG lleva 3 días recibida en parte: quedan 4 líneas (12 ud.) por dar de alta.",
                AvisoReposicionesRecibidasEnParte.Texto(reposicion, 3));
        }
    }
}
