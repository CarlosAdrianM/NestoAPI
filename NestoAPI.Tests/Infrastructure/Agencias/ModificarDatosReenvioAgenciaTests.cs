using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Controllers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#597 (incidencia 509, 06/10/26): cambiar retorno, reembolso o servicio de un envío con etiqueta
    /// viva. Matriz agencia × estado de la etiqueta × campo: Innovatrans/CTT se reenvían a la agencia, GLS viaja en
    /// el cierre o se pide a mano, entregado se bloquea (409) y pendiente sigue libre.
    /// </summary>
    [TestClass]
    public class ModificarDatosReenvioAgenciaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 6);
        private const string USUARIO = @"NUEVAVISION\aida";
        private const string ALBARAN_CTT = "0082800081234567";
        private const string ALBARAN_CTT_NUEVO = "0082800081239999";
        private const string ALBARAN_INNOVATRANS = "6522393001";

        private NVEntities db;
        private IContabilidadService contabilidad;
        private IFabricaAgenciasRemotas fabrica;
        private IAgenciaRemota ctt;
        private IAgenciaRemota innovatrans;
        private TramitacionEnviosService servicio;
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private readonly List<EnvioHistoria> historiaGuardada = new List<EnvioHistoria>();
        private readonly List<(bool Exito, string Error)> auditorias = new List<(bool, string)>();

        private class TransaccionDirecta : IAmbitoTransaccion
        {
            public Task<T> EjecutarAsync<T>(NVEntities db, Func<Task<T>> cuerpo) => cuerpo();
        }

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            contabilidad = A.Fake<IContabilidadService>();

            DbSet<EnviosAgencia> fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => fakeEnvios.Include(A<string>.Ignored)).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.ExtractosCliente).Returns(FakeDbSet(new List<ExtractoCliente>()));
            DbSet<EnvioHistoria> fakeHistoria = A.Fake<DbSet<EnvioHistoria>>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>.Ignored)).Invokes((EnvioHistoria h) => historiaGuardada.Add(h)).ReturnsLazily((EnvioHistoria h) => h);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => contabilidad.CrearLineas(db, A<List<PreContabilidad>>.Ignored)).Returns(Task.FromResult(1));
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", USUARIO)).Returns(Task.FromResult(90001));

            ctt = A.Fake<IAgenciaRemota>();
            innovatrans = A.Fake<IAgenciaRemota>();
            fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_CTT)).Returns(ctt);
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS)).Returns(innovatrans);
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_GLS)).Returns((IAgenciaRemota)null);

            servicio = new TramitacionEnviosService(db, contabilidad, () => HOY, A.Fake<IProcedimientosExtractoCliente>(), new TransaccionDirecta(),
                fabrica, (envio, agencia, exito, error) => { auditorias.Add((exito, error)); return Task.CompletedTask; });
        }

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            IQueryable<T> data = datos.AsQueryable();
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).Returns(new TestAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestAsyncEnumerator<T>(data.GetEnumerator()));
            return fakeSet;
        }

        private EnviosAgencia Envio(int agencia, short estado, string codigoBarras, string nombreAgencia, decimal reembolso = 0M)
        {
            EnviosAgencia envio = new EnviosAgencia
            {
                Numero = 249483,
                Agencia = agencia,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 927833,
                Vendedor = "NV ",
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodPostal = "28001",
                Poblacion = "MADRID",
                Peso = 2M,
                Bultos = 1,
                Reembolso = reembolso,
                Retorno = 0,
                Servicio = 48,
                Estado = estado,
                CodigoBarras = codigoBarras,
                Fecha = HOY,
                FechaEntrega = HOY.AddDays(1),
                Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                AgenciasTransporte = new AgenciaTransporte { Numero = agencia, Nombre = nombreAgencia, CuentaReembolsos = "5720000001 " }
            };
            envios.Add(envio);
            return envio;
        }

        private EnviosAgencia EnvioCtt(short estado = Constantes.Agencias.ESTADO_TRAMITADO, string codigoBarras = ALBARAN_CTT)
            => Envio(Constantes.Agencias.AGENCIA_CTT, estado, codigoBarras, "CTT");

        private EnviosAgencia EnvioGls(short estado)
            => Envio(Constantes.Agencias.AGENCIA_GLS, estado, "61771001234567", "ASM");

        private static ModificarDatosEnvioDTO SinCambios(EnviosAgencia envio) => new ModificarDatosEnvioDTO
        {
            Reembolso = envio.Reembolso,
            Retorno = envio.Retorno,
            Estado = envio.Estado,
            FechaEntrega = envio.FechaEntrega,
            Observaciones = "incidencia 509"
        };

        private static EtiquetaDataTrans Zpl() => new EtiquetaDataTrans { Tipo = "application/zpl", Codificacion = "base64", Contenido = "XlhBfkNJMTUw" };

        private void CttAcepta(Action<DatosEnvioRemoto> capturar = null)
        {
            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, ALBARAN_CTT))
                .Invokes((DatosEnvioRemoto d, string _) => capturar?.Invoke(d))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = true, Albaran = ALBARAN_CTT_NUEVO, Bultos = 1, Etiqueta = Zpl() }));
        }

        [TestMethod]
        public async Task CttViva_CambiaRetorno_ReenviaConAlbaranNuevoGuardaYDevuelveLaEtiqueta()
        {
            EnviosAgencia envio = EnvioCtt();
            DatosEnvioRemoto enviados = null;
            CttAcepta(d => enviados = d);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(1, enviados.Retorno, "a CTT va el envío YA con el retorno nuevo (adicional RET)");
            Assert.AreEqual("Calle Mayor 1", enviados.Direccion, "el resto del envío, tal cual está grabado");
            Assert.AreEqual(1, envio.Retorno);
            Assert.AreEqual(ALBARAN_CTT_NUEVO, envio.CodigoBarras, "CTT anula y registra: el envío se queda con el albarán nuevo");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            CollectionAssert.AreEqual(new[] { "Retorno", "CodigoBarras" }, historiaGuardada.Select(h => h.Campo).ToArray());
            Assert.AreEqual(ALBARAN_CTT, historiaGuardada[1].ValorAnterior, "queda rastro del albarán anulado");

            Assert.IsTrue(resultado.ReenviadoAAgencia);
            Assert.AreEqual(ALBARAN_CTT_NUEVO, resultado.Albaran);
            Assert.IsFalse(resultado.Reimpresion, "albarán nuevo, no reimpresión");
            Assert.AreEqual("XlhBfkNJMTUw", resultado.EtiquetaContenido);
            Assert.AreEqual("base64", resultado.EtiquetaCodificacion);
            StringAssert.Contains(resultado.Aviso, "Reenviado a CTT con albarán nuevo " + ALBARAN_CTT_NUEVO);
            StringAssert.Contains(resultado.Aviso, "etiqueta nueva");
            Assert.AreEqual(1, auditorias.Count);
            Assert.IsTrue(auditorias[0].Exito);
        }

        [TestMethod]
        public async Task CttViva_CambiaServicio_ReenviaConElServicioNuevo()
        {
            EnviosAgencia envio = EnvioCtt(Constantes.Agencias.ESTADO_EN_CURSO);
            DatosEnvioRemoto enviados = null;
            CttAcepta(d => enviados = d);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Servicio = 24;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(24, enviados.Servicio);
            Assert.AreEqual(24, envio.Servicio);
            Assert.AreEqual("48", historiaGuardada.Single(h => h.Campo == "Servicio").ValorAnterior);
            Assert.IsTrue(resultado.ReenviadoAAgencia);
        }

        [TestMethod]
        public async Task CttViva_AgenciaRechaza_502ConSuMotivoYBdIntacta()
        {
            EnviosAgencia envio = EnvioCtt();
            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, ALBARAN_CTT))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = false, Error = "CTT no permite anular el envío: ya recogido" }));
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO));

            Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
            StringAssert.Contains(ex.Message, "ya recogido");
            Assert.AreEqual(0, envio.Retorno);
            Assert.AreEqual(ALBARAN_CTT, envio.CodigoBarras);
            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            Assert.IsFalse(auditorias.Single().Exito, "el rechazo queda auditado");
        }

        [TestMethod]
        public async Task CttViva_ErrorDeConexion_502YBdIntacta()
        {
            EnviosAgencia envio = EnvioCtt();
            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored))
                .ThrowsAsync(new AgenciaRemotaException("CTT no responde"));
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO));

            Assert.AreEqual(HttpStatusCode.BadGateway, ex.StatusCode);
            Assert.AreEqual(0, envio.Retorno);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CttViva_SoloObservacionesEstadoYFecha_NoLlamaALaAgencia()
        {
            EnviosAgencia envio = EnvioCtt();
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Estado = Constantes.Agencias.ESTADO_INCIDENTADO;
            datos.FechaEntrega = HOY.AddDays(3);

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            Assert.AreEqual(Constantes.Agencias.ESTADO_INCIDENTADO, envio.Estado);
            Assert.IsNull(resultado.Aviso);
            Assert.IsFalse(resultado.ReenviadoAAgencia);
        }

        [TestMethod]
        public async Task CttViva_Rehusar_NoLlamaALaAgencia()
        {
            // «Rehusar»: el paquete vuelve; es contabilidad (RHS), no un cambio que la agencia tenga que leer.
            EnviosAgencia envio = EnvioCtt();
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 3;
            datos.Rehusar = true;

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO),
                "sin movimiento en el extracto el rehúse falla, pero por su motivo de siempre");

            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task InnovatransViva_CambiaReembolso_ModificaConElMismoAlbaranYContabiliza()
        {
            EnviosAgencia envio = Envio(Constantes.Agencias.AGENCIA_INNOVATRANS, Constantes.Agencias.ESTADO_TRAMITADO, ALBARAN_INNOVATRANS, "Innovatrans", reembolso: 0M);
            DatosEnvioRemoto enviados = null;
            A.CallTo(() => innovatrans.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, ALBARAN_INNOVATRANS))
                .Invokes((DatosEnvioRemoto d, string _) => enviados = d)
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = true, Albaran = ALBARAN_INNOVATRANS, Bultos = 1, Etiqueta = Zpl() }));
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 45.90M;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(45.90M, enviados.Reembolso);
            Assert.AreEqual(45.90M, envio.Reembolso);
            Assert.AreEqual(ALBARAN_INNOVATRANS, envio.CodigoBarras);
            Assert.AreEqual(90001, resultado.Asiento, "el cambio de reembolso se contabiliza como siempre");
            Assert.IsTrue(resultado.Reimpresion, "Innovatrans modifica: mismo albarán, etiqueta reimpresa");
            CollectionAssert.AreEqual(new[] { "Reembolso" }, resultado.CamposModificados);
            StringAssert.Contains(resultado.Aviso, "Modificado en Innovatrans");
        }

        [TestMethod]
        public async Task GlsSinCerrar_CambiaRetorno_LibreConAvisoDelCierre()
        {
            EnviosAgencia envio = EnvioGls(Constantes.Agencias.ESTADO_EN_CURSO);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(1, envio.Retorno);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
            Assert.AreEqual("El cambio viajará a GLS en el cierre del día", resultado.Aviso);
            Assert.IsFalse(resultado.ReenviadoAAgencia);
            Assert.AreEqual(0, auditorias.Count);
        }

        [TestMethod]
        public async Task GlsCerrada_CambiaRetorno_SoloEnNestoConAvisoDePedirloALaAgencia()
        {
            EnviosAgencia envio = EnvioGls(Constantes.Agencias.ESTADO_TRAMITADO);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(1, envio.Retorno);
            Assert.AreEqual(TramitacionEnviosService.AVISO_PEDIR_A_LA_AGENCIA, resultado.Aviso);
            StringAssert.Contains(resultado.Aviso, "solo se ha guardado en Nesto");
        }

        [TestMethod]
        public async Task Entregado_CambiaRetorno_409SinTocarNada()
        {
            EnviosAgencia envio = EnvioCtt(Constantes.Agencias.ESTADO_ENTREGADO);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual($"El envío {envio.Numero} ya está entregado/recogido: el siguiente albarán crea un envío nuevo con su propio retorno/reembolso.", ex.Message);
            Assert.AreEqual(0, envio.Retorno);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RetornoYaRecibido_CambiaReembolso_409()
        {
            EnviosAgencia envio = EnvioGls(Constantes.Agencias.ESTADO_TRAMITADO);
            envio.FechaRetornoRecibido = HOY;
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 10M;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual(0M, envio.Reembolso);
        }

        [TestMethod]
        public async Task Entregado_SoloEstado_SigueLibre()
        {
            EnviosAgencia envio = EnvioCtt(Constantes.Agencias.ESTADO_ENTREGADO);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Estado = Constantes.Agencias.ESTADO_TRAMITADO;

            _ = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public async Task Pendiente_CambiaRetorno_LibreSinLlamar()
        {
            EnviosAgencia envio = EnvioCtt((short)Constantes.Agencias.ESTADO_PENDIENTE, codigoBarras: null);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO);

            Assert.AreEqual(1, envio.Retorno);
            Assert.IsNull(resultado.Aviso);
            A.CallTo(() => ctt.ModificarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CttViva_AceptaPeroFallaLaBd_500ConLosDosAlbaranes()
        {
            EnviosAgencia envio = EnvioCtt();
            CttAcepta();
            A.CallTo(() => db.SaveChangesAsync()).ThrowsAsync(new InvalidOperationException("timeout"));
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Retorno = 1;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(envio.Numero, datos, USUARIO));

            Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            StringAssert.Contains(ex.Message, ALBARAN_CTT_NUEVO);
            StringAssert.Contains(ex.Message, ALBARAN_CTT);
        }
    }

    /// <summary>NestoAPI#597: el endpoint traduce los StatusCode de la excepción de negocio (409, 502).</summary>
    [TestClass]
    public class ModificarDatosEnvioControllerCodigosTests
    {
        private ITramitacionEnviosService fakeServicio;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            fakeServicio = A.Fake<ITramitacionEnviosService>();
            controller = new EnviosAgenciasController(A.Fake<NVEntities>(), fakeServicio);
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\aida") }, "Bearer"));
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.Conflict)]
        [DataRow(HttpStatusCode.BadGateway)]
        public async Task ModificarDatosEnvio_ExcepcionConCodigo_DevuelveEseCodigoConElTexto(HttpStatusCode codigo)
        {
            A.CallTo(() => fakeServicio.ModificarDatosAsync(A<int>.Ignored, A<ModificarDatosEnvioDTO>.Ignored, A<string>.Ignored))
                .Throws(new NestoBusinessException("motivo") { StatusCode = codigo });

            var resultado = await controller.ModificarDatosEnvio(1, new ModificarDatosEnvioDTO()) as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(codigo, resultado.StatusCode);
            Assert.AreEqual("motivo", resultado.Content);
        }
    }
}
