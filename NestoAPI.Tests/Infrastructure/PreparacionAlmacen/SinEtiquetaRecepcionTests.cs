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
            StringAssert.Contains(lineasAvisadas[0], "Reina ha recibido 1 unidad sin etiqueta del 17404 ROLLO PAPEL CAMILLA sin poder escanearla: no lleva etiqueta.");
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

        // ---- Ariadna#92 (sugerencia 1012, Carlos 04/11/26): producto con código de barras ----

        private void ConCodigoEnLaFicha(string codigoFicha, List<string> activos = null)
        {
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 81198)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1",
                Almacen = "REI",
                Traspaso = 81198,
                Lineas = new List<LineaReposicionDTO>
                {
                    new LineaReposicionDTO
                    {
                        Producto = "17404", Descripcion = "ROLLO PAPEL CAMILLA", Cantidad = 3,
                        CodigoBarras = codigoFicha, SinCodigo = codigoFicha == null, CodigosBarras = activos ?? new List<string>()
                    }
                }
            });
        }

        private void AvisoACompras()
        {
            A.CallTo(() => avisador.AvisarACompras(A<string>._, A<IEnumerable<string>>._))
                .ReturnsLazily((string titulo, IEnumerable<string> lineas) =>
                {
                    tituloAvisado = titulo;
                    lineasAvisadas = lineas.ToList();
                    return Task.FromResult(new List<string> { "Compras" });
                });
        }

        [TestMethod]
        public async Task Apuntar_NoLlevaConCodigoEnLaFicha_ArgumentExceptionYNoAvisa()
        {
            ConCodigoEnLaFicha("8436042870615");

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar("NoLleva"));

            Assert.AreEqual("El 17404 tiene código de barras 8436042870615: léalo del envase; «no lleva etiqueta» es solo para los productos sin código.", ex.Message);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.AvisarACompras(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar_NoLlevaConCodigoSoloEnProductosCodigosBarras_ArgumentException()
        {
            var codigos = A.Fake<NestoAPI.Infraestructure.Productos.IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.CodigosActivos("1", A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, List<string>>
            {
                ["17404"] = new List<string> { "8436042870615" }
            });
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, null, codigos);

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar("NoLleva"));

            StringAssert.StartsWith(ex.Message, "El 17404 tiene código de barras 8436042870615");
        }

        [TestMethod]
        public async Task Apuntar_NoLlevaConSoloCodigoInterno_AvisaAlOrigenComoAntes()
        {
            // El interno (29…) va en la etiqueta que pone el origen: sin ella no se puede leer
            ConCodigoEnLaFicha(null, new List<string> { NestoAPI.Infraestructure.Productos.CodigoBarrasInterno.DeReferencia("17404") });

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar("NoLleva");

            Assert.AreEqual("Apuntada 1 unidad sin etiqueta del 17404: se ha avisado a Algete y a Compras.", resultado.Mensaje);
            A.CallTo(() => avisador.AvisarACompras(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar_NoSeLeeConCodigo_SoloACompras()
        {
            ConCodigoEnLaFicha("8436042870615");
            AvisoACompras();

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar("NoSeLee");

            Assert.AreEqual("Apuntada 1 unidad sin etiqueta del 17404: se ha avisado a Compras.", resultado.Mensaje);
            CollectionAssert.AreEqual(new[] { "Compras" }, resultado.Avisados);
            Assert.AreEqual("Reposición 81198 (ALG → REI): el código del 17404 no se lee o no coincide", tituloAvisado);
            Assert.IsTrue(lineasAvisadas.Any(l => l.Contains("8436042870615")));
            StringAssert.Contains(lineasAvisadas[0], "el código no se lee");
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar_OtroConCodigo_SoloAComprasConElComentario()
        {
            ConCodigoEnLaFicha("8436042870615");
            AvisoACompras();

            _ = await Apuntar("Otro", "el envase trae otro código");

            StringAssert.Contains(lineasAvisadas[0], "otro motivo: el envase trae otro código.");
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostSinEtiqueta_NoLlevaConCodigo_400ConElCodigo()
        {
            ConCodigoEnLaFicha("8436042870615");

            var resultado = await Controlador(servicio).PostSinEtiqueta("REPO", "81198", new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva" }, "REI")
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.StartsWith(resultado.Message, "El 17404 tiene código de barras 8436042870615: léalo del envase");
        }

        [TestMethod]
        public async Task Avisador_AvisarACompras_SoloCompras_EnNestoYEnAriadna()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var grupos = new Dictionary<string, List<string>>
            {
                [Constantes.GruposSeguridad.ALMACEN] = new List<string> { "Alfredo" },
                [Constantes.GruposSeguridad.COMPRAS] = new List<string> { "Laura" }
            };
            var aviso = new AvisadorSinEtiqueta(push, g => grupos[g], (e, a) => Task.FromResult(new List<string> { "NoDebe" }));

            List<string> avisados = await aviso.AvisarACompras("título", new[] { "línea" });

            CollectionAssert.AreEqual(new[] { "Compras" }, avisados);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Laura", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("Laura", Constantes.Aplicaciones.ARIADNA, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>.That.Matches(u => u.Contains("Alfredo") || u.Contains("NoDebe")), A<string>._, A<NotificacionPushDTO>._))
                .MustNotHaveHappened();
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

        // ---- Idempotencia (revisión 30/10): la PDA repite tras un timeout ----

        /// <summary>Como el de Modificaciones, en memoria.</summary>
        private class IdempotenciaEnMemoria : IRepositorioIdempotenciaSinEtiqueta
        {
            public readonly Dictionary<string, string> Guardadas = new Dictionary<string, string>();

            public async Task<ResultadoSinEtiquetaRecepcionDTO> UnaVez(string clave, Func<Task<ResultadoSinEtiquetaRecepcionDTO>> procesar)
            {
                if (Guardadas.TryGetValue(clave, out string json))
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<ResultadoSinEtiquetaRecepcionDTO>(json);
                }
                ResultadoSinEtiquetaRecepcionDTO resultado = await procesar();
                Guardadas[clave] = Newtonsoft.Json.JsonConvert.SerializeObject(resultado);
                return resultado;
            }
        }

        private static SinEtiquetaRecepcionDTO ConClave(string clave)
            => new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva", ClaveIdempotencia = clave };

        [TestMethod]
        public async Task Apuntar_MismaClaveDosVeces_AvisaUnaVezYDevuelveLaPrimeraRespuesta()
        {
            var idempotencia = new IdempotenciaEnMemoria();
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, idempotencia);

            ResultadoSinEtiquetaRecepcionDTO primera = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("a1b2"), @"NUEVAVISION\Reina");
            ResultadoSinEtiquetaRecepcionDTO segunda = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("a1b2"), @"NUEVAVISION\Reina");

            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual(primera.Mensaje, segunda.Mensaje);
            CollectionAssert.AreEqual(primera.Avisados, segunda.Avisados);
            Assert.AreEqual("1|REI|REPO|81198|a1b2", idempotencia.Guardadas.Keys.Single());
        }

        [TestMethod]
        public async Task Apuntar_ClavesDistintasOSinClave_AvisaCadaVez()
        {
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, new IdempotenciaEnMemoria());

            _ = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("uno"), "Reina");
            _ = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("dos"), "Reina");
            _ = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave(null), "Reina");
            _ = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("  "), "Reina");

            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustHaveHappened(4, Times.Exactly);
        }

        [TestMethod]
        public async Task Apuntar_PrimeraVezFalla_NoSeApuntaYElReintentoAvisa()
        {
            var idempotencia = new IdempotenciaEnMemoria();
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, idempotencia);

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("REPO", "1", "REI", "81198",
                new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "Raro", ClaveIdempotencia = "k" }, "Reina"));
            _ = await servicio.Apuntar("REPO", "1", "REI", "81198", ConClave("k"), "Reina");

            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Apuntar_ClaveDemasiadoLarga_400()
        {
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, new IdempotenciaEnMemoria());

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("REPO", "1", "REI", "81198",
                ConClave(new string('x', ServicioSinEtiquetaRecepcion.LONGITUD_MAXIMA_CLAVE + 1)), "Reina"));
        }

        [TestMethod]
        public async Task PostSinEtiqueta_MismaClave_200ConLaMismaRespuesta()
        {
            RecepcionesController controller = Controlador(new ServicioSinEtiquetaRecepcion(reposiciones, avisador, new IdempotenciaEnMemoria()));

            var primera = await controller.PostSinEtiqueta("REPO", "81198", ConClave("g-1"), "REI") as OkNegotiatedContentResult<ResultadoSinEtiquetaRecepcionDTO>;
            var segunda = await controller.PostSinEtiqueta("REPO", "81198", ConClave("g-1"), "REI") as OkNegotiatedContentResult<ResultadoSinEtiquetaRecepcionDTO>;

            Assert.IsNotNull(primera);
            Assert.IsNotNull(segunda);
            Assert.AreEqual(primera.Content.Mensaje, segunda.Content.Mensaje);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustHaveHappenedOnceExactly();
        }

        // ---- NestoAPI#839 (sugerencia 1113): varias unidades de una vez, como mucho las que faltan, y un solo aviso ----

        private IRepositorioLecturasRecepcion ConLeido(int leidoDel17404)
        {
            IRepositorioLecturasRecepcion lecturas = A.Fake<IRepositorioLecturasRecepcion>();
            A.CallTo(() => lecturas.TablaExiste()).Returns(true);
            A.CallTo(() => lecturas.Abrir(A<ClaveRecepcionEnCurso>.That.Matches(c =>
                    c.Empresa == "1" && c.Tipo == "REPO" && c.Documento == "81198" && c.Almacen == "REI"), A<string>._, A<string>._))
                .Returns(new List<FilaLecturaRecepcionEnCurso>
                {
                    new FilaLecturaRecepcionEnCurso { Producto = null, Cantidad = 0 },
                    new FilaLecturaRecepcionEnCurso { Producto = "17404", Cantidad = leidoDel17404 },
                    new FilaLecturaRecepcionEnCurso { Producto = "OTRO", Cantidad = 5 }
                });
            servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador, lecturas: lecturas);
            return lecturas;
        }

        [TestMethod]
        public async Task Apuntar839_VariasDentroDeLoQueFalta_LasCuentaYUnSoloAvisoConLaCantidad()
        {
            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar(unidades: 3);

            Assert.AreEqual("Apuntadas 3 unidades sin etiqueta del 17404: se ha avisado a Algete y a Compras.", resultado.Mensaje);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("Reposición 81198 (ALG → REI): llegan 3 unidades sin etiqueta del 17404", tituloAvisado);
            StringAssert.Contains(lineasAvisadas[0], "Reina ha recibido 3 unidades sin etiqueta del 17404 ROLLO PAPEL CAMILLA sin poder escanearlas");
        }

        [TestMethod]
        public async Task Apuntar839_ConLoLeidoEnCurso_ElTopeEsLoQueFalta()
        {
            _ = ConLeido(1);

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar(unidades: 2);

            StringAssert.StartsWith(resultado.Mensaje, "Apuntadas 2 unidades sin etiqueta del 17404");
        }

        [TestMethod]
        public async Task Apuntar839_MasDeLoQueFalta_ArgumentExceptionYNiSeCuentaNiSeAvisa()
        {
            _ = ConLeido(1);

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(unidades: 3));

            Assert.AreEqual("No se pueden apuntar 3 unidades sin etiqueta del 17404: solo faltan 2.", ex.Message);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .MustNotHaveHappened();
            A.CallTo(() => avisador.AvisarACompras(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar839_SinLecturasEnCurso_MasDeLoEnviado_ArgumentException()
        {
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(unidades: 4));

            StringAssert.Contains(ex.Message, "solo faltan 3");
        }

        [TestMethod]
        public async Task Apuntar839_NoFaltaNinguna_UnaDeMasSiVariasNo()
        {
            _ = ConLeido(3);

            ResultadoSinEtiquetaRecepcionDTO una = await Apuntar(unidades: 1);
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Apuntar(unidades: 2));

            StringAssert.StartsWith(una.Mensaje, "Apuntada 1 unidad sin etiqueta del 17404");
            StringAssert.Contains(ex.Message, "ya no falta ninguna");
        }

        [TestMethod]
        public async Task Apuntar839_UnaSola_NoMiraLoLeido()
        {
            IRepositorioLecturasRecepcion lecturas = ConLeido(3);

            _ = await Apuntar(unidades: 1);

            A.CallTo(() => lecturas.Abrir(A<ClaveRecepcionEnCurso>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Apuntar839_SinUnidades_Una()
        {
            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar(unidades: 0);

            Assert.AreEqual("Apuntada 1 unidad sin etiqueta del 17404: se ha avisado a Algete y a Compras.", resultado.Mensaje);
            Assert.AreEqual("Reposición 81198 (ALG → REI): llega sin etiqueta el 17404", tituloAvisado);
        }

        [TestMethod]
        public async Task Apuntar839_SiFallaLeerLoLeido_ElTopeEsLoEnviado()
        {
            IRepositorioLecturasRecepcion lecturas = ConLeido(0);
            A.CallTo(() => lecturas.Abrir(A<ClaveRecepcionEnCurso>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin BD"));

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar(unidades: 3);

            StringAssert.StartsWith(resultado.Mensaje, "Apuntadas 3 unidades");
        }

        [TestMethod]
        public async Task Apuntar839_ConCodigoNoSeLee_UnAvisoACompras_ConLaCantidad()
        {
            ConCodigoEnLaFicha("8436042870615");
            AvisoACompras();

            ResultadoSinEtiquetaRecepcionDTO resultado = await Apuntar(motivo: "NoSeLee", unidades: 2);

            Assert.AreEqual("Apuntadas 2 unidades sin etiqueta del 17404: se ha avisado a Compras.", resultado.Mensaje);
            A.CallTo(() => avisador.AvisarACompras(A<string>._, A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(tituloAvisado, "2 unidades del 17404");
            StringAssert.Contains(lineasAvisadas[0], "ha recibido 2 unidades sin etiqueta del 17404");
        }

        [TestMethod]
        public async Task PostSinEtiqueta839_MasDeLoQueFalta_400ConElTexto()
        {
            var resultado = await Controlador(servicio).PostSinEtiqueta("REPO", "81198",
                new SinEtiquetaRecepcionDTO { Producto = "17404", Motivo = "NoLleva", Unidades = 4 }, "REI") as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.StartsWith(resultado.Message, "No se pueden apuntar 4 unidades sin etiqueta del 17404");
        }
    }
}
