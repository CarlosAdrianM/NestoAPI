using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Controllers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Nesto#340 (Agencias, slice A4.4): modificar reembolso/retorno/estado/fecha de entrega de un
    /// envío tramitado, calcado de AgenciasViewModel.modificarEnvio + contabilizarModificacionReembolso
    /// (los últimos bloques de Entity Framework de Nesto). Con los SPs y la transacción tras interfaces
    /// se prueba el flujo ENTERO con dobles en memoria: historia, desliquidar, deshago/rehago, RHS.
    /// </summary>
    [TestClass]
    public class ModificacionEnvioServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 18);
        private static readonly DateTime FECHA_ENVIO = new DateTime(2026, 9, 15);
        private const string USUARIO = @"NUEVAVISION\laura";

        private NVEntities db;
        private IContabilidadService contabilidad;
        private IProcedimientosExtractoCliente procedimientos;
        private TramitacionEnviosService servicio;
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private readonly List<ExtractoCliente> extracto = new List<ExtractoCliente>();
        private readonly List<EnvioHistoria> historiaGuardada = new List<EnvioHistoria>();
        private List<PreContabilidad> apuntes;

        /// <summary>En los tests la transacción es transparente: se ejecuta el cuerpo y punto.</summary>
        private class TransaccionDirecta : IAmbitoTransaccion
        {
            public Task<T> EjecutarAsync<T>(NVEntities db, Func<Task<T>> cuerpo) => cuerpo();
        }

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            contabilidad = A.Fake<IContabilidadService>();
            procedimientos = A.Fake<IProcedimientosExtractoCliente>();

            DbSet<EnviosAgencia> fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => fakeEnvios.Include(A<string>.Ignored)).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.ExtractosCliente).Returns(FakeDbSet(extracto));
            DbSet<EnvioHistoria> fakeHistoria = A.Fake<DbSet<EnvioHistoria>>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>.Ignored)).Invokes((EnvioHistoria h) => historiaGuardada.Add(h)).ReturnsLazily((EnvioHistoria h) => h);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            A.CallTo(() => contabilidad.CrearLineas(db, A<List<PreContabilidad>>.Ignored))
                .Invokes((NVEntities d, List<PreContabilidad> l) => apuntes = l).Returns(Task.FromResult(2));
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", USUARIO)).Returns(Task.FromResult(88140));

            servicio = new TramitacionEnviosService(db, contabilidad, () => HOY, procedimientos, new TransaccionDirecta());
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

        private EnviosAgencia EnvioTramitado(decimal reembolso = 121.50M, DateTime? fechaPago = null)
        {
            EnviosAgencia envio = new EnviosAgencia
            {
                Numero = 247975,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Pedido = 922175,
                Vendedor = "NV ",
                Reembolso = reembolso,
                Retorno = 1,
                Estado = 1,
                Fecha = FECHA_ENVIO,
                FechaEntrega = new DateTime(2026, 9, 16),
                FechaPagoReembolso = fechaPago,
                Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                AgenciasTransporte = new AgenciaTransporte { Numero = 12, Nombre = "Innovatrans", CuentaReembolsos = "5720000001 " }
            };
            envios.Add(envio);
            return envio;
        }

        private static ModificarDatosEnvioDTO SinCambios(EnviosAgencia envio) => new ModificarDatosEnvioDTO
        {
            Reembolso = envio.Reembolso,
            Retorno = envio.Retorno,
            Estado = envio.Estado,
            FechaEntrega = envio.FechaEntrega,
            Observaciones = "prueba"
        };

        /// <summary>El pago que se contabilizó al tramitar (liquidado: Importe ≠ ImportePdte) y la factura pendiente.</summary>
        private void ExtractoConPagoYFactura(EnviosAgencia envio, decimal importeAnterior)
        {
            extracto.Add(new ExtractoCliente
            {
                Nº_Orden = 7001,
                Empresa = envio.Empresa,
                Número = envio.Cliente,
                Contacto = envio.Contacto,
                Fecha = envio.Fecha,
                TipoApunte = "3",
                // En SQL un Nº_Documento nulo no rompe el StartsWith del filtro de cursos; en memoria sí.
                Nº_Documento = "922175",
                Concepto = TramitacionEnviosService.GenerarConcepto(envio),
                Importe = -importeAnterior,
                ImportePdte = 0,
                Estado = "NRM"
            });
            extracto.Add(new ExtractoCliente
            {
                Nº_Orden = 5001,
                Empresa = envio.Empresa,
                Número = envio.Cliente,
                Contacto = envio.Contacto,
                Fecha = envio.Fecha,
                TipoApunte = "1",
                Nº_Documento = "NV2612946",
                Concepto = "Factura NV2612946",
                Importe = importeAnterior,
                ImportePdte = importeAnterior,
                FechaVto = new DateTime(2026, 10, 15),
                CCC = "1  ",
                Ruta = "16 ",
                Estado = "NRM"
            });
        }

        [TestMethod]
        public async Task ModificarDatos_CambiaElReembolso_HistoriaDesliquidaDeshaceYRehace()
        {
            EnviosAgencia envio = EnvioTramitado(121.50M);
            ExtractoConPagoYFactura(envio, 121.50M);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 80M;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(247975, datos, USUARIO);

            // Historia: una fila, con el valor anterior como lo escribía el cliente (moneda).
            Assert.AreEqual(1, historiaGuardada.Count);
            Assert.AreEqual("Reembolso", historiaGuardada[0].Campo);
            StringAssert.StartsWith(historiaGuardada[0].ValorAnterior, "121,50");
            Assert.AreEqual("prueba", historiaGuardada[0].Observaciones);
            Assert.AreEqual(USUARIO, historiaGuardada[0].Usuario);
            Assert.AreEqual(247975, historiaGuardada[0].NumeroEnvio);
            Assert.AreEqual(80M, envio.Reembolso);

            // El pago anterior estaba liquidado: se desliquida ANTES de los apuntes.
            A.CallTo(() => procedimientos.DesliquidarAsync(db, "1", 7001)).MustHaveHappenedOnceExactly();

            // Deshago (debe, asiento 1, liquida contra el pago) + Rehago (haber, asiento 2, liquida contra la factura).
            Assert.AreEqual(2, apuntes.Count);
            PreContabilidad deshago = apuntes[0];
            Assert.AreEqual("1", deshago.Empresa);
            Assert.AreEqual("_Reembolso", deshago.Diario);
            Assert.AreEqual(1, deshago.Asiento);
            Assert.AreEqual("3", deshago.TipoApunte);
            Assert.AreEqual("2", deshago.TipoCuenta);
            Assert.AreEqual("15191", deshago.Nº_Cuenta);
            Assert.AreEqual("0", deshago.Contacto);
            Assert.AreEqual(HOY, deshago.Fecha);
            Assert.AreEqual(HOY, deshago.FechaVto);
            Assert.AreEqual(121.50M, deshago.Debe);
            Assert.AreEqual(0M, deshago.Haber);
            Assert.AreEqual("Deshago Reembolso 922175 a Innovatrans c/15191", deshago.Concepto);
            Assert.AreEqual("5720000001", deshago.Contrapartida);
            Assert.AreEqual("EFC", deshago.FormaPago);
            Assert.AreEqual("NV ", deshago.Vendedor, "sin trimear, como en el cliente");
            Assert.AreEqual("922175", deshago.Nº_Documento);
            Assert.AreEqual("ALG", deshago.Delegación, "los 'varios' de la empresa, no ALG a pelo: así lo hacía el cliente en ESTE apunte");
            Assert.AreEqual("DIR", deshago.FormaVenta);
            Assert.AreEqual(7001, deshago.Liquidado);
            Assert.AreEqual(USUARIO, deshago.Usuario);

            PreContabilidad rehago = apuntes[1];
            Assert.AreEqual(2, rehago.Asiento);
            Assert.AreEqual(0M, rehago.Debe);
            Assert.AreEqual(80M, rehago.Haber);
            Assert.AreEqual("Rehago Reembolso 922175 a Innovatrans c/15191", rehago.Concepto);
            Assert.AreEqual(5001, rehago.Liquidado, "la factura pendiente, elegida con el reembolso ANTERIOR como hacía el cliente");

            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", USUARIO)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(88140, resultado.Asiento);
            CollectionAssert.AreEqual(new[] { "Reembolso" }, resultado.CamposModificados);
            Assert.IsFalse(resultado.Rehusado);
        }

        [TestMethod]
        public async Task ModificarDatos_ReembolsoAnteriorCero_SoloRehace()
        {
            EnviosAgencia envio = EnvioTramitado(0M);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 50M;

            _ = await servicio.ModificarDatosAsync(247975, datos, USUARIO);

            A.CallTo(() => procedimientos.DesliquidarAsync(A<NVEntities>.Ignored, A<string>.Ignored, A<int>.Ignored)).MustNotHaveHappened();
            Assert.AreEqual(1, apuntes.Count);
            Assert.AreEqual(50M, apuntes[0].Haber);
            Assert.AreEqual(2, apuntes[0].Asiento);
            Assert.IsNull(apuntes[0].Liquidado, "con reembolso anterior 0 el cliente buscaba entre los negativos y no liquidaba nada");
        }

        [TestMethod]
        public async Task ModificarDatos_SoloFechaEntregaYRetorno_HistoriaSinContabilidad()
        {
            EnviosAgencia envio = EnvioTramitado();
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.FechaEntrega = new DateTime(2026, 9, 21);
            datos.Retorno = 2;
            datos.RetornoAnteriorDescripcion = "Sin retorno";

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(247975, datos, USUARIO);

            CollectionAssert.AreEqual(new[] { "Retorno", "FechaEntrega" }, historiaGuardada.Select(h => h.Campo).ToArray(), "una fila por campo (el cliente solo guardaba la última)");
            Assert.AreEqual("Sin retorno", historiaGuardada[0].ValorAnterior, "la descripción del tipo de retorno anterior, que la sabe el cliente");
            StringAssert.Contains(historiaGuardada[1].ValorAnterior, "16/09/2026");
            Assert.AreEqual(new DateTime(2026, 9, 21), envio.FechaEntrega);
            Assert.AreEqual(2, envio.Retorno);
            A.CallTo(() => contabilidad.CrearLineas(A<NVEntities>.Ignored, A<List<PreContabilidad>>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, resultado.Asiento);
        }

        [TestMethod]
        public async Task ModificarDatos_SinCambios_NoEscribeNada()
        {
            EnviosAgencia envio = EnvioTramitado();

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(247975, SinCambios(envio), USUARIO);

            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            StringAssert.Contains(resultado.Mensaje, "no tenía nada que modificar");
        }

        [TestMethod]
        public async Task ModificarDatos_YaCobrado_RechazaSinTocarNada()
        {
            EnviosAgencia envio = EnvioTramitado(fechaPago: new DateTime(2026, 9, 17));
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 1M;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(247975, datos, USUARIO));

            StringAssert.Contains(ex.Message, "ya está cobrado");
            Assert.AreEqual(121.50M, envio.Reembolso);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ModificarDatos_Rehusar_PoneElEfectoDeLaFacturaEnRHS()
        {
            // «Rehusar» en el cliente: reembolso a 0, retorno obligatorio de la agencia, estado igual.
            EnviosAgencia envio = EnvioTramitado(121.50M);
            ExtractoConPagoYFactura(envio, 121.50M);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Reembolso = 0;
            datos.Retorno = 3;
            datos.Rehusar = true;

            ResultadoModificacionEnvio resultado = await servicio.ModificarDatosAsync(247975, datos, USUARIO);

            A.CallTo(() => procedimientos.ModificarEfectoClienteAsync(db, 5001, new DateTime(2026, 10, 15), "1  ", "16 ", "RHS", "Factura NV2612946"))
                .MustHaveHappenedOnceExactly();
            Assert.IsTrue(resultado.Rehusado);
            Assert.AreEqual(1, apuntes.Count, "con reembolso nuevo 0 solo hay deshago");
            Assert.AreEqual(121.50M, apuntes[0].Debe);
        }

        [TestMethod]
        public async Task ModificarDatos_RehusarSinMovimientoEnElExtracto_RechazaConMotivo()
        {
            EnviosAgencia envio = EnvioTramitado(0M);
            ModificarDatosEnvioDTO datos = SinCambios(envio);
            datos.Rehusar = true;

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(247975, datos, USUARIO));

            StringAssert.Contains(ex.Message, "rehusado");
            A.CallTo(() => procedimientos.ModificarEfectoClienteAsync(A<NVEntities>.Ignored, A<int>.Ignored, A<DateTime?>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ModificarDatos_EnvioInexistente_Rechaza()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarDatosAsync(999, new ModificarDatosEnvioDTO(), USUARIO));
        }

        [TestMethod]
        public void ModificarDatosEnvioDTO_ContratoDelJson_NombresQueMandaNesto()
        {
            string[] propiedades = typeof(ModificarDatosEnvioDTO).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Estado", "FechaEntrega", "Observaciones", "Reembolso", "Rehusar", "Retorno", "RetornoAnteriorDescripcion" }, propiedades);
            string[] respuesta = typeof(ResultadoModificacionEnvio).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "Asiento", "CamposModificados", "Mensaje", "Numero", "Rehusado" }, respuesta);
        }
    }

    /// <summary>POST api/EnviosAgencias/{id}/ModificarDatos: contrato del endpoint con el servicio fakeado.</summary>
    [TestClass]
    public class ModificarDatosEnvioControllerTests
    {
        private ITramitacionEnviosService fakeServicio;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            fakeServicio = A.Fake<ITramitacionEnviosService>();
            controller = new EnviosAgenciasController(A.Fake<NVEntities>(), fakeServicio);
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\laura") }, "Bearer"));
        }

        [TestMethod]
        public async Task ModificarDatosEnvio_Exito_DevuelveElResultadoConElUsuarioDelJwt()
        {
            ModificarDatosEnvioDTO datos = new ModificarDatosEnvioDTO { Reembolso = 80M };
            A.CallTo(() => fakeServicio.ModificarDatosAsync(247975, datos, @"NUEVAVISION\laura"))
                .Returns(Task.FromResult(new ResultadoModificacionEnvio { Numero = 247975, Asiento = 88140 }));

            var resultado = await controller.ModificarDatosEnvio(247975, datos) as OkNegotiatedContentResult<ResultadoModificacionEnvio>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(88140, resultado.Content.Asiento);
        }

        [TestMethod]
        public async Task ModificarDatosEnvio_MotivoDeNegocio_Devuelve400ConElTexto()
        {
            A.CallTo(() => fakeServicio.ModificarDatosAsync(A<int>.Ignored, A<ModificarDatosEnvioDTO>.Ignored, A<string>.Ignored))
                .Throws(new NestoBusinessException("No se puede modificar este envío, porque ya está cobrado"));

            var resultado = await controller.ModificarDatosEnvio(1, new ModificarDatosEnvioDTO()) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("No se puede modificar este envío, porque ya está cobrado", resultado.Message);
        }

        [TestMethod]
        public async Task ModificarDatosEnvio_SinCuerpo_Devuelve400()
        {
            Assert.IsInstanceOfType(await controller.ModificarDatosEnvio(1, null), typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeServicio.ModificarDatosAsync(A<int>.Ignored, A<ModificarDatosEnvioDTO>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }
    }
}
