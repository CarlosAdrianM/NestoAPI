using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// POST api/EnviosAgencias/{id}/Tramitar: tramitación server-side de un envío con la agencia
    /// remota. Se fakean la BD (FakeItEasy) y la factory de agencias remotas, así que NO se llama a
    /// la API real de Innovatrans.
    /// </summary>
    [TestClass]
    public class TramitarEnvioTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IAgenciaRemota fakeAgencia;
        private EnviosAgenciasController controller;
        private AgenciaLlamadaWeb auditoria;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            fakeAgencia = A.Fake<IAgenciaRemota>();
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>());
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();

            // Captura la auditoría que se inserta, para poder inspeccionarla.
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.Ignored))
                .Invokes((AgenciaLlamadaWeb l) => auditoria = l)
                .ReturnsLazily((AgenciaLlamadaWeb l) => l);

            controller = new EnviosAgenciasController(db, fakeFabrica);
            controller.Request = new System.Net.Http.HttpRequestMessage
            {
                RequestUri = new System.Uri("http://localhost/api/EnviosAgencias/1/Tramitar")
            };
        }

        private void ConEnvio(EnviosAgencia envio)
        {
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
        }

        private static EnviosAgencia EnvioPendiente() => new EnviosAgencia
        {
            Numero = 1,
            Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS,
            Pedido = 12345,
            Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
            Nombre = "CLIENTE",
            Direccion = "Calle Mayor 1",
            CodPostal = "28001",
            Poblacion = "MADRID",
            Telefono = "600000000",
            Peso = 1.5m,
            Bultos = 1,
            Reembolso = -1m,
            CodigoBarras = null
        };

        private static EtiquetaDataTrans EtiquetaZpl() => new EtiquetaDataTrans
        {
            Tipo = "application/zpl", Codificacion = "base64", Contenido = "XlhBfkNJMTUw"
        };

        [TestMethod]
        public async Task Tramitar_EnvioInexistente_DevuelveNotFound()
        {
            A.CallTo(() => fakeEnvios.FindAsync(99)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.TramitarEnvio(99);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        private static EnviosAgencia EnvioTramitado() => new EnviosAgencia
        {
            Numero = 1,
            Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS,
            Pedido = 12345,
            Estado = Constantes.Agencias.ESTADO_TRAMITADO,
            CodigoBarras = "6522393001",
            Nombre = "CLIENTE", Direccion = "Calle Mayor 1", CodPostal = "28001", Poblacion = "MADRID",
            Telefono = "600000000", Peso = 1.5m, Bultos = 1
        };

        [TestMethod]
        public async Task ActualizarSeguimiento_EnvioInexistente_DevuelveNotFound()
        {
            A.CallTo(() => fakeEnvios.FindAsync(99)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.ActualizarSeguimiento(99);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task ActualizarSeguimiento_SinAlbaran_DevuelveBadRequest()
        {
            var envio = EnvioTramitado();
            envio.CodigoBarras = null;
            ConEnvio(envio);

            var resultado = await controller.ActualizarSeguimiento(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ActualizarSeguimiento_ConSeguimiento_ActualizaEstadoYDevuelveOk()
        {
            var envio = EnvioTramitado();   // entra Tramitado (1)
            ConEnvio(envio);

            var fakeSeguimiento = A.Fake<ISeguimientoAgenciaRemota>();
            A.CallTo(() => fakeSeguimiento.ConsultarSeguimientoAsync(A<string>._))
                .Returns(Task.FromResult(new SeguimientoEnvioRemoto
                {
                    Estado = EstadoEnvioSeguimiento.Entregado,
                    FechaEntrega = new System.DateTime(2026, 6, 26)
                }));
            A.CallTo(() => fakeFabrica.CrearSeguimiento(envio.Agencia)).Returns(fakeSeguimiento);

            var resultado = await controller.ActualizarSeguimiento(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<SeguimientoEnvioRemoto>));
            Assert.AreEqual((short)EstadoEnvioSeguimiento.Entregado, envio.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_AgenciaSinGestionRemota_DevuelveBadRequest()
        {
            EnviosAgencia envio = EnvioPendiente();
            envio.Agencia = Constantes.Agencias.AGENCIA_GLS;
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns(null);

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Tramitar_Exito_GuardaAlbaranBultosEstadoYDevuelveZpl()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = true, Albaran = "0123456789", Bultos = 2, Etiqueta = EtiquetaZpl()
                }));

            var resultado = await controller.TramitarEnvio(1);

            var ok = resultado as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("0123456789", ok.Content.Albaran);
            Assert.AreEqual(2, ok.Content.Bultos);
            Assert.IsFalse(ok.Content.Reimpresion);
            Assert.AreEqual("XlhBfkNJMTUw", ok.Content.EtiquetaContenido);

            // Persistencia en EnviosAgencia.
            Assert.AreEqual("0123456789", envio.CodigoBarras);
            Assert.AreEqual((short)2, envio.Bultos);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado);
            Assert.AreEqual(0m, envio.Reembolso); // sentinel -1 -> 0 al tramitar
            // Auditoría con éxito.
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => l.Exito && l.Agencia == "Innovatrans"))).MustHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_Exito_AuditaElSoapCrudoDeLosIntercambios()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = true, Albaran = "0123456789", Bultos = 1, Etiqueta = EtiquetaZpl() }));
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>
            {
                new IntercambioRemoto { Operacion = "InsertarEnvios", Url = "http://dtx/Envios", Peticion = "<peticion-insert/>", Respuesta = "<respuesta-insert/>" },
                new IntercambioRemoto { Operacion = "BusquedaEtiquetas", Url = "http://dtx/Etiquetas", Peticion = "<peticion-etiqueta/>", Respuesta = "<respuesta-etiqueta/>" }
            });

            await controller.TramitarEnvio(1);

            Assert.IsNotNull(auditoria);
            Assert.IsTrue(auditoria.Exito);
            // Las peticiones crudas de ambos intercambios van en CuerpoLlamada.
            StringAssert.Contains(auditoria.CuerpoLlamada, "<peticion-insert/>");
            StringAssert.Contains(auditoria.CuerpoLlamada, "<peticion-etiqueta/>");
            // Las respuestas crudas en CuerpoRespuesta.
            StringAssert.Contains(auditoria.CuerpoRespuesta, "<respuesta-insert/>");
            StringAssert.Contains(auditoria.CuerpoRespuesta, "<respuesta-etiqueta/>");
        }

        [TestMethod]
        public async Task Tramitar_FalloConErrorLargo_AuditoriaRespetaLosLimitesYNoEsNull()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            // Error muy largo (>255) y sin intercambios: antes reventaba el guardado de la auditoría
            // (TextoRespuestaError nvarchar(255) NOT NULL, CuerpoRespuesta NOT NULL).
            string errorLargo = new string('x', 400);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Throws(new NestoAPI.Infraestructure.Agencias.Innovatrans.DataTransException(errorLargo));

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.IsNotNull(auditoria);
            Assert.IsFalse(auditoria.Exito);
            Assert.IsTrue(auditoria.TextoRespuestaError.Length <= 255, "TextoRespuestaError no puede superar 255.");
            Assert.IsNotNull(auditoria.CuerpoRespuesta, "CuerpoRespuesta es NOT NULL.");
            Assert.IsNotNull(auditoria.UrlLlamada);
            Assert.IsTrue(auditoria.Usuario.Length <= 30);
        }

        [TestMethod]
        public async Task Tramitar_YaTramitado_SoloReimprimeSinReinsertar()
        {
            EnviosAgencia envio = EnvioPendiente();
            envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;
            envio.CodigoBarras = "0123456789"; // ya tiene albarán
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.ReimprimirAsync("0123456789", null, null)).Returns(Task.FromResult(EtiquetaZpl()));

            var resultado = await controller.TramitarEnvio(1);

            var ok = resultado as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Reimpresion);
            // NO se reinsertó el envío.
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_InsertOkPeroEtiquetaFalla_GuardaAlbaranParaNoReinsertar()
        {
            // El envío YA se registró en la agencia (albarán) pero la etiqueta falló (ZPL no disponible).
            // El controller DEBE persistir el albarán y dejar el envío En curso, para que un reintento
            // reimprima en vez de reinsertar (envío fantasma + cobro doble). Y devolver el error.
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Albaran = "6520139001",
                    Bultos = 1,
                    Error = "El envío se registró en Innovatrans (albarán 6520139001) pero no devolvió una etiqueta ZPL válida."
                }));

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.AreEqual("6520139001", envio.CodigoBarras, "Hay que guardar el albarán aunque la etiqueta falle.");
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado, "Queda En curso, no se reinsertará.");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => !l.Exito))).MustHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_FalloEnInsercion_NoGuardaAlbaranYAuditaError()
        {
            EnviosAgencia envio = EnvioPendiente();
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = false, Error = "codError 500" }));

            var resultado = await controller.TramitarEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.IsNull(envio.CodigoBarras, "No debe guardar albarán si la inserción falló.");
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado, "El envío queda pendiente, no a medias.");
            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.That.Matches(l => !l.Exito))).MustHaveHappened();
        }
    }
}
