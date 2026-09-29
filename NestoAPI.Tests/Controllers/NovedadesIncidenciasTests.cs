using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#558: «🐞 Algo no funciona» en Novedades. Mismo formulario y mismo almacenamiento que las
    /// sugerencias, pero con categoría Incidencia y un contexto automático (versión, pantalla y errores
    /// de ELMAH del usuario de la última hora) que solo ven Dirección / Informática.
    /// </summary>
    [TestClass]
    public class NovedadesIncidenciasTests
    {
        private IServicioNovedades servicio;
        private IServicioNotificacionesPush notificaciones;
        private ILectorParametrosUsuario lector;
        private NovedadesController controller;
        private SugerenciaNovedadAGrabar grabada;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioNovedades>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, Constantes.ParametrosUsuario.AVISAR_ACTIVIDAD_NOVEDADES_A))
                .Returns(null); // sin fila: Carlos
            A.CallTo(() => servicio.CrearSugerencia(A<SugerenciaNovedadAGrabar>._))
                .Invokes((SugerenciaNovedadAGrabar s) => grabada = s).Returns(558);
            controller = new NovedadesController(servicio, A.Fake<IServicioFeedbackNovedades>())
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Notificaciones = notificaciones,
                LectorParametros = lector
            };
        }

        private void ComoUsuarioDeNesto(string usuario = "NUEVAVISION\\Alfredo", params string[] roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, usuario),
                new Claim(ClaimTypes.Name, usuario),
                new Claim(ClaimTypes.AuthenticationMethod, "Windows")
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        }

        private static NuevaSugerenciaNovedadDTO Incidencia(string texto = "Al guardar el pedido se queda colgado") =>
            new NuevaSugerenciaNovedadDTO
            {
                Texto = texto,
                EsIncidencia = true,
                VersionCliente = "Nesto 1.10.33.0",
                Pantalla = "PlantillaVenta"
            };

        // ---- Crear ----

        [TestMethod]
        public async Task PostSugerencia_Incidencia_SeGuardaConCategoriaIncidenciaYElContexto()
        {
            ComoUsuarioDeNesto();
            A.CallTo(() => servicio.LeerErroresElmah(A<IReadOnlyList<string>>._, A<DateTime>._, A<int>._))
                .Returns(new List<ErrorElmahResumen>
                {
                    new ErrorElmahResumen { TimeUtc = DateTime.UtcNow, Type = "System.Data.SqlClient.SqlException", Message = "Timeout expired" }
                });

            var resultado = await controller.PostSugerencia(Incidencia()) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.EsIncidencia);
            Assert.AreEqual(ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, resultado.Content.Categoria);
            Assert.AreEqual(ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, grabada.Categoria);
            Assert.AreEqual("Al guardar el pedido se queda colgado", grabada.TextoOriginal, "el texto del usuario, sin tocar");
            StringAssert.Contains(grabada.Contexto, "Versión: Nesto 1.10.33.0");
            StringAssert.Contains(grabada.Contexto, "Pantalla: PlantillaVenta");
            StringAssert.Contains(grabada.Contexto, "System.Data.SqlClient.SqlException: Timeout expired");
        }

        [TestMethod]
        public async Task PostSugerencia_Incidencia_BuscaLosErroresDelUsuarioConYSinDominioDeLaUltimaHora()
        {
            ComoUsuarioDeNesto();
            IReadOnlyList<string> usuarios = null;
            DateTime desde = DateTime.MinValue;
            int maximo = 0;
            A.CallTo(() => servicio.LeerErroresElmah(A<IReadOnlyList<string>>._, A<DateTime>._, A<int>._))
                .Invokes((IReadOnlyList<string> u, DateTime d, int m) => { usuarios = u; desde = d; maximo = m; })
                .Returns(new List<ErrorElmahResumen>());

            _ = await controller.PostSugerencia(Incidencia());

            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Alfredo", "Alfredo" }, usuarios.ToArray());
            Assert.IsTrue(Math.Abs((DateTime.UtcNow.AddHours(-1) - desde).TotalMinutes) < 1, "desde hace una hora, en UTC");
            Assert.AreEqual(5, maximo);
            StringAssert.Contains(grabada.Contexto, "Errores de la última hora: ninguno.");
        }

        [TestMethod]
        public async Task PostSugerencia_Incidencia_SiElmahFalla_SeGuardaIgualConLoDemas()
        {
            ComoUsuarioDeNesto();
            A.CallTo(() => servicio.LeerErroresElmah(A<IReadOnlyList<string>>._, A<DateTime>._, A<int>._))
                .Throws(new InvalidOperationException("sin permiso en ELMAH_Error"));

            var resultado = await controller.PostSugerencia(Incidencia()) as OkNegotiatedContentResult<SugerenciaNovedadDTO>;

            Assert.IsNotNull(resultado, "el contexto nunca rompe el aviso");
            StringAssert.Contains(grabada.Contexto, "Versión: Nesto 1.10.33.0");
            StringAssert.Contains(grabada.Contexto, "no se pudieron consultar");
        }

        [TestMethod]
        public async Task PostSugerencia_Sugerencia_NoLeeElmahNiGuardaContexto()
        {
            ComoUsuarioDeNesto();

            _ = await controller.PostSugerencia(new NuevaSugerenciaNovedadDTO { Texto = "Un botón para duplicar", Pantalla = "PlantillaVenta" });

            Assert.AreEqual(ReglasSugerenciasNovedades.CATEGORIA_SUGERENCIA, grabada.Categoria);
            Assert.IsNull(grabada.Contexto);
            A.CallTo(() => servicio.LeerErroresElmah(A<IReadOnlyList<string>>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSugerencia_Incidencia_AvisaAlSupervisorDeQueAlgoNoFunciona()
        {
            ComoUsuarioDeNesto();

            _ = await controller.PostSugerencia(Incidencia());

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", "Nesto",
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == "Alfredo ha avisado de algo que no funciona"
                    && n.Cuerpo == "Al guardar el pedido se queda colgado" && n.Datos["novedadId"] == "558")))
                .MustHaveHappenedOnceExactly();
        }

        // ---- Listar: el contexto solo para Dirección / Informática ----

        private void ConUnaIncidenciaEnLaLista()
        {
            A.CallTo(() => servicio.LeerSugerencias(A<bool>._)).Returns(new List<NovedadConSugerenciaFila>
            {
                new NovedadConSugerenciaFila
                {
                    Id = 558, Categoria = "Incidencia", Titulo = "Se queda colgado", Ambito = "Nesto",
                    TextoOriginal = "Se queda colgado", Estado = "Pendiente", Contexto = "Versión: Nesto 1.10.33.0"
                },
                new NovedadConSugerenciaFila
                {
                    Id = 526, Categoria = "Nuevo", Titulo = "Duplicar", Ambito = "Nesto", TextoOriginal = "Duplicar", Estado = "Pendiente"
                }
            });
        }

        [TestMethod]
        public void GetSugerencias_Informatica_VeElContextoYDistingueLasIncidencias()
        {
            ComoUsuarioDeNesto("NUEVAVISION\\Carlos", "NUEVAVISION\\Informatica");
            ConUnaIncidenciaEnLaLista();

            var lista = ((OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetSugerencias()).Content;

            SugerenciaNovedadDTO incidencia = lista.Single(s => s.Id == 558);
            Assert.IsTrue(incidencia.EsIncidencia);
            Assert.AreEqual("Versión: Nesto 1.10.33.0", incidencia.Contexto);
            Assert.IsFalse(lista.Single(s => s.Id == 526).EsIncidencia);
        }

        [TestMethod]
        public void GetSugerencias_UnUsuarioCualquiera_NoVeElContexto()
        {
            ComoUsuarioDeNesto();
            ConUnaIncidenciaEnLaLista();

            var lista = ((OkNegotiatedContentResult<List<SugerenciaNovedadDTO>>)controller.GetSugerencias()).Content;

            SugerenciaNovedadDTO incidencia = lista.Single(s => s.Id == 558);
            Assert.IsTrue(incidencia.EsIncidencia, "sí sabe que es una incidencia");
            Assert.IsNull(incidencia.Contexto, "los errores de ELMAH de otro usuario no se enseñan");
        }

        // ---- Corregir ----

        [TestMethod]
        public void Normalizar_IncidenciaConVersion_PasaACorregido()
        {
            var cambios = new ActualizarSugerenciaNovedadDTO { Version = "1.10.34.0", Categoria = "incidencia" };

            Assert.IsNull(ReglasSugerenciasNovedades.Normalizar(cambios));
            Assert.AreEqual(ReglasSugerenciasNovedades.CATEGORIA_CORREGIDO, cambios.Categoria);
            Assert.AreEqual(ReglasSugerenciasNovedades.ESTADO_IMPLEMENTADA, cambios.Estado);
        }

        [TestMethod]
        public void Normalizar_ReclasificarComoIncidenciaSinVersion_Vale()
        {
            var cambios = new ActualizarSugerenciaNovedadDTO { Categoria = "Incidencia" };

            Assert.IsNull(ReglasSugerenciasNovedades.Normalizar(cambios));
            Assert.AreEqual(ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, cambios.Categoria);
        }

        // ---- Reglas puras ----

        [TestMethod]
        public void UsuariosElmah_DeLaApp_TalCualYConDominio()
        {
            CollectionAssert.AreEqual(new[] { "Manuel", "NUEVAVISION\\Manuel" },
                ReglasSugerenciasNovedades.UsuariosElmah("Manuel", "NUEVAVISION\\").ToArray());
        }

        [TestMethod]
        public void ComponerContexto_RecortaLosMensajesLargosYQuitaLosSaltosDeLinea()
        {
            string contexto = ReglasSugerenciasNovedades.ComponerContexto("Nesto 1.10.33.0", null, new[]
            {
                new ErrorElmahResumen { TimeUtc = new DateTime(2026, 9, 29, 8, 5, 0), Type = "System.Exception", Message = "Línea 1\r\n" + new string('x', 500) }
            });

            string[] lineas = contexto.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.AreEqual(3, lineas.Length, "versión, cabecera de errores y el error (sin pantalla)");
            Assert.AreEqual("Errores de la última hora (1):", lineas[1]);
            StringAssert.StartsWith(lineas[2], "- 10:05 System.Exception: Línea 1 xxx", "hora de España (CEST = UTC+2)");
            Assert.IsTrue(lineas[2].EndsWith("…"));
            Assert.IsTrue(lineas[2].Length < 250);
        }

        [TestMethod]
        public void ComponerContexto_SinVersion_LoDice()
        {
            StringAssert.StartsWith(ReglasSugerenciasNovedades.ComponerContexto(null, null, new List<ErrorElmahResumen>()),
                "Versión: (desconocida)");
        }
    }
}
