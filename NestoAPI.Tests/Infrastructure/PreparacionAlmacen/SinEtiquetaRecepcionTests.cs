using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#762 (sugerencia 909): una reposición se recibe solo escaneando; lo que llega sin etiqueta se cuenta unidad a
    /// unidad con su motivo, y cada caso avisa al origen (que tenía que etiquetarlo) y a Compras.
    /// </summary>
    [TestClass]
    public class SinEtiquetaRecepcionTests
    {
        private IServicioRecepcionReposiciones reposiciones;
        private IAvisadorSinEtiqueta avisador;
        private ServicioSinEtiquetaRecepcion servicio;
        private string tituloAvisado;
        private List<string> lineasAvisadas;
        private string creadorAvisado;
        private string origenAvisado;

        [TestInitialize]
        public void Preparar()
        {
            reposiciones = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 81198)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1",
                Almacen = "REI",
                Traspaso = 81198,
                Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "17404", Descripcion = "ROLLO PAPEL CAMILLA", Cantidad = 3 } }
            });
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 81199)).Returns((RecepcionReposicionDTO)null);
            A.CallTo(() => reposiciones.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 81198, Origen = "ALG", NombreOrigen = "Algete", Usuario = @"NUEVAVISION\Andre" }
            });
            avisador = A.Fake<IAvisadorSinEtiqueta>();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .ReturnsLazily((string e, string origen, string nombre, string creador, string titulo, IEnumerable<string> lineas) =>
                {
                    tituloAvisado = titulo;
                    lineasAvisadas = lineas.ToList();
                    creadorAvisado = creador;
                    origenAvisado = origen;
                    return Task.FromResult(new List<string> { "Algete", "Compras" });
                });
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador);
        }

        private Task<ResultadoSinEtiquetaRecepcionDTO> Apuntar(string motivo = "NoLleva", string comentario = null, string producto = "17404",
            string tipo = "REPO", int unidades = 0)
            => servicio.Apuntar(tipo, "1", "REI", "81198", new SinEtiquetaRecepcionDTO
            {
                Producto = producto,
                Motivo = motivo,
                Comentario = comentario,
                Unidades = unidades,
                Dispositivo = "Móvil de Reina"
            }, @"NUEVAVISION\Reina");

        [TestMethod]
        public async Task Apuntar_AvisaAlOrigenYAComprasConElMotivo()
        {
            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar();

            Assert.AreEqual("Apuntada 1 unidad sin etiqueta del 17404: se ha avisado a Algete y a Compras.", resultado.Mensaje);
            CollectionAssert.AreEqual(new[] { "Algete", "Compras" }, resultado.Avisados);
            Assert.AreEqual("Reposición 81198 (ALG → REI): llega sin etiqueta el 17404", tituloAvisado);
            StringAssert.Contains(lineasAvisadas[0], "Reina ha recibido 1 unidad del 17404 ROLLO PAPEL CAMILLA sin poder escanearla: no lleva etiqueta.");
            Assert.AreEqual(@"NUEVAVISION\Andre", creadorAvisado);
            Assert.AreEqual("ALG", origenAvisado);
        }

        [TestMethod]
        public async Task Apuntar_LaEtiquetaNoSeLee_ComprasCompruebaElCodigo()
        {
            await Apuntar("noselee");

            StringAssert.Contains(lineasAvisadas[0], "la etiqueta no se lee");
            Assert.IsTrue(lineasAvisadas.Any(l => l.StartsWith("Compras: compruebe el código de barras", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task Apuntar_Otro_ConSuComentario()
        {
            await Apuntar("Otro", "  es una lima, no se puede pegar ");

            StringAssert.Contains(lineasAvisadas[0], "otro motivo: es una lima, no se puede pegar.");
        }

        [TestMethod]
        public async Task Apuntar_OtroSinComentario_ArgumentException()
        {
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar("Otro"));

            Assert.AreEqual("Con «Otro», escriba el motivo.", ex.Message);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar_MotivoDesconocido_ArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar("PorqueSi"));
        }

        [TestMethod]
        public async Task Apuntar_ProductoQueNoViene_ArgumentException()
        {
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(producto: "99999"));

            StringAssert.Contains(ex.Message, "no viene en la reposición 81198");
        }

        [TestMethod]
        public async Task Apuntar_UnaCompra_ArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(tipo: "COMP"));
        }

        [TestMethod]
        public async Task Apuntar_ReposicionQueNoEstaPendiente_KeyNotFound()
        {
            await Assert.ThrowsExceptionAsync<KeyNotFoundException>(() => servicio.Apuntar("REPO", "1", "REI", "81199",
                new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva" }, "Reina"));
        }

        [TestMethod]
        public async Task Apuntar_DemasiadasUnidades_ArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(unidades: ServicioSinEtiquetaRecepcion.MAXIMO_UNIDADES + 1));
        }

        [TestMethod]
        public async Task Apuntar_SiFallaElAviso_SeApuntaIgualYLoDice()
        {
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .Throws(new InvalidOperationException("sin buzón"));

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar();

            StringAssert.Contains(resultado.Mensaje, "no se ha podido avisar a nadie");
        }

        // ---- El avisador: a quién ----

        [TestMethod]
        public async Task Avisador_OrigenAlgete_AlmacenYComprasYQuienLaPreparo_EnNestoYEnAriadna()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var grupos = new Dictionary<string, List<string>>
            {
                [Constantes.GruposSeguridad.ALMACEN] = new List<string> { "Alfredo", "Andre" },
                [Constantes.GruposSeguridad.COMPRAS] = new List<string> { "Laura" }
            };
            var aviso = new AvisadorSinEtiqueta(push, g => grupos[g], (e, a) => Task.FromResult(new List<string> { "NoDebe" }));

            List<string> avisados = await aviso.Avisar("1", "ALG", "Algete", @"NUEVAVISION\Andre", "título", new[] { "línea" });

            CollectionAssert.AreEqual(new[] { "Algete", "Compras" }, avisados);
            foreach (string u in new[] { "Alfredo", "Andre", "Laura" })
            {
                A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\" + u, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
                A.CallTo(() => push.GuardarEnBuzonDeUsuario(u, Constantes.Aplicaciones.ARIADNA, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            }
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>.That.Contains("NoDebe"), A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Avisador_OrigenTienda_SuGenteYCompras_NoElGrupoAlmacen()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var grupos = new Dictionary<string, List<string>>
            {
                [Constantes.GruposSeguridad.ALMACEN] = new List<string> { "Alfredo" },
                [Constantes.GruposSeguridad.COMPRAS] = new List<string> { "Laura" }
            };
            var aviso = new AvisadorSinEtiqueta(push, g => grupos[g], (e, a) => Task.FromResult(a == "REI" ? new List<string> { "Reina" } : new List<string>()));

            List<string> avisados = await aviso.Avisar("1", "REI", "Reina", "ReposicionAutomatica", "título", new[] { "línea" });

            CollectionAssert.AreEqual(new[] { "Reina", "Compras" }, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Reina", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == AvisadorSinEtiqueta.TIPO_NOTIFICACION && n.Cuerpo == "línea"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Alfredo", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ---- El endpoint ----

        private static RecepcionesController Controlador(IServicioSinEtiquetaRecepcion sinEtiqueta)
        {
            var controller = new RecepcionesController(A.Fake<IServicioRecepciones>(), sinEtiqueta: sinEtiqueta)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.RequestContext.Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\Reina") }, "prueba"));
            return controller;
        }

        [TestMethod]
        public async Task PostSinEtiqueta_200ConElMensaje()
        {
            RecepcionesController controller = Controlador(servicio);

            var resultado = await controller.PostSinEtiqueta("REPO", "81198", new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva" }, "rei")
                as OkNegotiatedContentResult<ResultadoSinEtiquetaRecepcionDTO>;

            Assert.IsNotNull(resultado);
            StringAssert.StartsWith(resultado.Content.Mensaje, "Apuntada 1 unidad sin etiqueta del 17404");
        }

        [TestMethod]
        public async Task PostSinEtiqueta_MotivoNoValido_400ConElTexto()
        {
            var resultado = await Controlador(servicio).PostSinEtiqueta("REPO", "81198", new SinEtiquetaRecepcionDTO { Producto = "17404" }, "REI")
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.StartsWith(resultado.Message, "El motivo tiene que ser");
        }

        [TestMethod]
        public async Task PostSinEtiqueta_NoPendiente_404()
        {
            IHttpActionResult resultado = await Controlador(servicio).PostSinEtiqueta("REPO", "81199",
                new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva" }, "REI");
            HttpResponseMessage respuesta = await resultado.ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task PostSinEtiqueta_SinServicio_404()
        {
            IHttpActionResult resultado = await Controlador(null).PostSinEtiqueta("REPO", "81198",
                new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva" }, "REI");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }
    }
}
