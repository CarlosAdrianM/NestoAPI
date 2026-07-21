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
    /// POST api/EnviosAgencias/{id}/Anular y /Modificar (#316/#317): deshacer o corregir un envío
    /// YA registrado en la agencia remota. Regla central: API primero, BD después — si la agencia
    /// rechaza (p. ej. la ventana de edición del día ya cerró), nuestra BD queda intacta y el motivo
    /// de la agencia llega tal cual al usuario.
    /// </summary>
    [TestClass]
    public class AnularModificarEnvioTests
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
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(fakeAgencia);

            A.CallTo(() => fakeLlamadas.Add(A<AgenciaLlamadaWeb>.Ignored))
                .Invokes((AgenciaLlamadaWeb l) => auditoria = l)
                .ReturnsLazily((AgenciaLlamadaWeb l) => l);

            controller = new EnviosAgenciasController(db, fakeFabrica);
            controller.Request = new System.Net.Http.HttpRequestMessage
            {
                RequestUri = new System.Uri("http://localhost/api/EnviosAgencias/1/Anular")
            };
        }

        private void ConEnvio(EnviosAgencia envio)
        {
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
        }

        // Envío YA registrado en la agencia (albarán asignado, En curso): el caso del pedido 922531.
        private static EnviosAgencia EnvioEnCurso() => new EnviosAgencia
        {
            Numero = 1,
            Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS,
            Pedido = 922531,
            Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO,
            CodigoBarras = "6522393001",
            Nombre = "CLIENTE",
            Direccion = "Calle Mayor 1",
            CodPostal = "28925", // CP incorrecto que el usuario quiere corregir
            Poblacion = "ALCORCON",
            Telefono = "600000000",
            Peso = 1.5m,
            Bultos = 1,
            Reembolso = 0m
        };

        private static EtiquetaDataTrans EtiquetaZpl() => new EtiquetaDataTrans
        {
            Tipo = "application/zpl", Codificacion = "base64", Contenido = "XlhBfkNJMTUw"
        };

        #region Anular (#316)

        [TestMethod]
        public async Task Anular_EnvioInexistente_DevuelveNotFound()
        {
            A.CallTo(() => fakeEnvios.FindAsync(99)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.AnularEnvio(99);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Anular_AgenciaSinGestionRemota_DevuelveBadRequest()
        {
            var envio = EnvioEnCurso();
            envio.Agencia = Constantes.Agencias.AGENCIA_GLS;
            ConEnvio(envio);
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns(null);

            var resultado = await controller.AnularEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Anular_EtiquetaPendienteSinAlbaran_DevuelveBadRequest()
        {
            // Las pendientes se borran con el DELETE normal: anular es solo para registradas.
            var envio = EnvioEnCurso();
            envio.CodigoBarras = null;
            envio.Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE;
            ConEnvio(envio);

            var resultado = await controller.AnularEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeAgencia.AnularAsync(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_ReembolsoYaPagado_DevuelveBadRequestSinLlamarALaAgencia()
        {
            var envio = EnvioEnCurso();
            envio.FechaPagoReembolso = new System.DateTime(2026, 7, 10);
            ConEnvio(envio);

            var resultado = await controller.AnularEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeAgencia.AnularAsync(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_Exito_DevuelveElEnvioAPendienteYSinAlbaran()
        {
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            A.CallTo(() => fakeAgencia.AnularAsync("6522393001"))
                .Returns(Task.FromResult(new ResultadoOperacionRemota { Exito = true }));

            var resultado = await controller.AnularEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_PENDIENTE, envio.Estado,
                "Anulado en la agencia = vuelve a etiqueta pendiente (se puede corregir, re-tramitar o borrar).");
            Assert.AreEqual(string.Empty, envio.CodigoBarras, "El albarán anulado no debe quedar en el envío.");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            Assert.IsNotNull(auditoria, "Debe auditar la anulación.");
            Assert.IsTrue(auditoria.Exito);
            StringAssert.Contains(auditoria.CuerpoLlamada, "Anular");
        }

        [TestMethod]
        public async Task Anular_AgenciaRechaza_NoTocaLaBDYDevuelveElMotivoTalCual()
        {
            // Caso clave para logística: la ventana de edición del día ya cerró (codError 413).
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            A.CallTo(() => fakeAgencia.AnularAsync(A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoOperacionRemota
                {
                    Exito = false,
                    Error = "Innovatrans no ha podido anular el envío (albarán 6522393001): Excedido el tiempo de borrado"
                }));

            var resultado = await controller.AnularEnvio(1);

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            StringAssert.Contains(contenido.Content, "Excedido el tiempo de borrado");
            // BD intacta: el envío sigue registrado.
            Assert.AreEqual("6522393001", envio.CodigoBarras);
            Assert.AreEqual((short)Constantes.Agencias.ESTADO_EN_CURSO, envio.Estado);
            Assert.IsNotNull(auditoria);
            Assert.IsFalse(auditoria.Exito);
        }

        [TestMethod]
        public async Task Anular_ErrorDeConexion_DevuelveBadGatewayYAudita()
        {
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            A.CallTo(() => fakeAgencia.AnularAsync(A<string>.Ignored))
                .Throws(new DataTransException("No hay conexión con DataTrans"));

            var resultado = await controller.AnularEnvio(1);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.AreEqual("6522393001", envio.CodigoBarras, "Con error de conexión la BD queda intacta.");
            Assert.IsNotNull(auditoria);
            Assert.IsFalse(auditoria.Exito);
        }

        #endregion

        #region Modificar (#317)

        [TestMethod]
        public async Task Modificar_EtiquetaPendiente_DevuelveBadRequest()
        {
            // Las pendientes se corrigen con ActualizarDireccionEtiqueta (sin llamar a la agencia).
            var envio = EnvioEnCurso();
            envio.CodigoBarras = null;
            envio.Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE;
            ConEnvio(envio);

            var resultado = await controller.ModificarEnvio(1, new ModificarEnvioAgenciaDTO { CodigoPostal = "28001" });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeAgencia.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Modificar_SinDatos_DevuelveBadRequest()
        {
            var resultado = await controller.ModificarEnvio(1, null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Modificar_Exito_MandaLosDatosCorregidosPersisteYDevuelveEtiqueta()
        {
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            DatosEnvioRemoto enviados = null;
            A.CallTo(() => fakeAgencia.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, "6522393001"))
                .Invokes((DatosEnvioRemoto d, string _) => enviados = d)
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = true, Albaran = "6522393001", Bultos = 1, Etiqueta = EtiquetaZpl()
                }));

            var resultado = await controller.ModificarEnvio(1, new ModificarEnvioAgenciaDTO
            {
                CodigoPostal = "28001",
                Poblacion = "MADRID"
            });

            var ok = resultado as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual("6522393001", ok.Content.Albaran);
            Assert.AreEqual("XlhBfkNJMTUw", ok.Content.EtiquetaContenido, "Modificar re-etiqueta: la etiqueta lleva el CP impreso.");
            // A la agencia van los datos corregidos, con los no informados conservados.
            Assert.AreEqual("28001", enviados.CodigoPostal);
            Assert.AreEqual("MADRID", enviados.Poblacion);
            Assert.AreEqual("Calle Mayor 1", enviados.Direccion, "Los campos no informados se conservan.");
            // Y se persisten en la BD.
            Assert.AreEqual("28001", envio.CodPostal);
            Assert.AreEqual("MADRID", envio.Poblacion);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            Assert.IsNotNull(auditoria);
            Assert.IsTrue(auditoria.Exito);
            StringAssert.Contains(auditoria.CuerpoLlamada, "Modificar");
        }

        [TestMethod]
        public async Task Modificar_AgenciaRechaza_NoPersisteNadaYDevuelveElMotivo()
        {
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            A.CallTo(() => fakeAgencia.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = "Innovatrans rechazó la modificación del envío (albarán 6522393001): Excedido el tiempo"
                }));

            var resultado = await controller.ModificarEnvio(1, new ModificarEnvioAgenciaDTO { CodigoPostal = "28001" });

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            StringAssert.Contains(contenido.Content, "Excedido el tiempo");
            Assert.AreEqual("28925", envio.CodPostal, "Si la agencia rechaza, la BD conserva la dirección original.");
            Assert.IsNotNull(auditoria);
            Assert.IsFalse(auditoria.Exito);
        }

        [TestMethod]
        public async Task Modificar_ModificadoPeroSinEtiqueta_PersisteLosDatosNuevos()
        {
            // La agencia SÍ aplicó la modificación (albarán en el resultado) pero la etiqueta falló:
            // la BD debe quedarse con la dirección nueva (el envío ya viaja con ella) y el usuario
            // reimprime con Tramitar (idempotente).
            var envio = EnvioEnCurso();
            ConEnvio(envio);
            A.CallTo(() => fakeAgencia.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Albaran = "6522393001",
                    Bultos = 1,
                    Error = "El envío se modificó en Innovatrans (albarán 6522393001) pero no devolvió una etiqueta ZPL válida."
                }));

            var resultado = await controller.ModificarEnvio(1, new ModificarEnvioAgenciaDTO { CodigoPostal = "28001" });

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<string>));
            Assert.AreEqual("28001", envio.CodPostal, "Modificado en la agencia = persistir aunque falle la etiqueta.");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        #endregion

        #region RestarReembolso (NestoAPI#335 / Nesto#420)

        // Al quitar la comisión contra reembolso del pedido, el usuario puede restar su importe
        // del reembolso del envío. Regla: RESTAR, nunca recalcular (respeta importes a mano).

        private EnviosAgencia EnvioConReembolso(short estado, decimal reembolso)
        {
            var envio = EnvioEnCurso();
            envio.Estado = estado;
            envio.Reembolso = reembolso;
            ConEnvio(envio);
            return envio;
        }

        [TestMethod]
        public async Task RestarReembolso_EnvioVivo_RestaYDevuelveElNuevoImporte()
        {
            // El caso real 920278: reembolso 73,86 con una comisión de 3,15 recién quitada
            var envio = EnvioConReembolso((short)Constantes.Agencias.ESTADO_EN_CURSO, 73.86m);

            var resultado = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 3.15m })
                as OkNegotiatedContentResult<RestarReembolsoResponseDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(70.71m, resultado.Content.Reembolso);
            Assert.AreEqual(70.71m, envio.Reembolso, "Debe restar, no recalcular");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task RestarReembolso_EnvioTramitado_BadRequestSinTocarNada()
        {
            // Carlos 21/07/26: un envío ya TRAMITADO no se toca — la agencia ya tiene el importe
            // y es lo que va a cobrar. En ese caso lo que procede es abonar la comisión después.
            var envio = EnvioConReembolso(Constantes.Agencias.ESTADO_TRAMITADO, 73.86m);

            var resultado = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 3.15m })
                as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "tramitado");
            StringAssert.Contains(resultado.Message, "abone la comisión");
            Assert.AreEqual(73.86m, envio.Reembolso);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RestarReembolso_EnvioEntregado_BadRequestSinTocarNada()
        {
            var envio = EnvioConReembolso(Constantes.Agencias.ESTADO_ENTREGADO, 73.86m);

            var resultado = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 3.15m });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(73.86m, envio.Reembolso);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RestarReembolso_EnvioPendiente_SePuedeAjustar()
        {
            // Pendiente (etiqueta aún no tramitada): la agencia todavía no tiene el importe.
            var envio = EnvioConReembolso((short)Constantes.Agencias.ESTADO_PENDIENTE, 50m);

            var resultado = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 10m })
                as OkNegotiatedContentResult<RestarReembolsoResponseDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(40m, envio.Reembolso);
        }

        [TestMethod]
        public async Task RestarReembolso_ImporteMayorQueElReembolso_BadRequest()
        {
            var envio = EnvioConReembolso((short)Constantes.Agencias.ESTADO_EN_CURSO, 2.00m);

            var resultado = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 3.15m });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(2.00m, envio.Reembolso);
        }

        [TestMethod]
        public async Task RestarReembolso_SinReembolsoOImporteInvalido_BadRequest()
        {
            var envio = EnvioConReembolso((short)Constantes.Agencias.ESTADO_EN_CURSO, 0m);

            var sinReembolso = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 3.15m });
            var importeCero = await controller.RestarReembolso(1, new RestarReembolsoDTO { Importe = 0m });

            Assert.IsInstanceOfType(sinReembolso, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(importeCero, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task RestarReembolso_EnvioInexistente_NotFound()
        {
            A.CallTo(() => fakeEnvios.FindAsync(99)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.RestarReembolso(99, new RestarReembolsoDTO { Importe = 3.15m });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion
    }
}
