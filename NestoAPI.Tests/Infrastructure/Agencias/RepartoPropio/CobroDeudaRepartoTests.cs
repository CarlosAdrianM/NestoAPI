using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#766 (Ariadna#67, petición de Miguel 30/10/26): el repartidor cobra en efectivo una deuda de un cliente, como en la
    /// caja de Nesto (CajasViewModel.ContabilizarCobro): efectos elegidos, importe, lo que sobra a cuenta, contrapartida a SU caja.
    /// </summary>
    [TestClass]
    public class CobroDeudaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 30, 12, 0, 0);
        private const string MIGUEL = "Miguel";
        private const string CAJA_MIGUEL = "57000032";

        private IRepositorioCobrosReparto repositorio;
        private ILectorParametrosUsuario parametros;
        private ServicioCobrosReparto servicio;
        private List<PreContabilidad> registradas;
        private List<EfectoCobroReparto> efectosBd;

        [TestInitialize]
        public void Setup()
        {
            efectosBd = new List<EfectoCobroReparto>();
            registradas = null;
            repositorio = A.Fake<IRepositorioCobrosReparto>();
            A.CallTo(() => repositorio.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCobroDeudaDTO>(null));
            A.CallTo(() => repositorio.LeerEfectos(A<IReadOnlyCollection<int>>._))
                .ReturnsLazily((IReadOnlyCollection<int> ids) => Task.FromResult(efectosBd.Where(e => ids.Contains(e.Id)).ToList()));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._))
                .ReturnsLazily((Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCobroDeudaDTO r) =>
                {
                    registradas = lineas;
                    return Task.FromResult(4321);
                });
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", MIGUEL, "CajaDefecto")).Returns(CAJA_MIGUEL);
            A.CallTo(() => parametros.LeerParametro("1", MIGUEL, "DiarioCaja")).Returns("_Caja");
            A.CallTo(() => parametros.LeerParametro("1", MIGUEL, "DelegaciónDefecto")).Returns("ALG");
            A.CallTo(() => parametros.LeerParametro("1", MIGUEL, "FormaVentaDefecto")).Returns("VAR");
            servicio = new ServicioCobrosReparto(repositorio, parametros, () => AHORA);
        }

        private EfectoCobroReparto Efecto(int id, decimal pendiente, string documento = "NV26001", string efecto = "1", string cliente = "15191",
            string empresa = "1  ", string tipo = "2")
        {
            var e = new EfectoCobroReparto
            {
                Id = id, Empresa = empresa, Cliente = cliente + "   ", Contacto = "0  ", TipoApunte = tipo, Documento = documento + "  ", Efecto = efecto,
                Importe = pendiente, ImportePendiente = pendiente, Vencimiento = new DateTime(2026, 10, 15), Delegacion = "ALG", FormaVenta = "TEL",
                Ruta = "AT", Vendedor = "NV", Estado = "NRM"
            };
            efectosBd.Add(e);
            return e;
        }

        private static CobroDeudaRepartoDTO Peticion(decimal importe, params int[] efectos)
            => new CobroDeudaRepartoDTO { Cliente = "15191", Efectos = efectos.ToList(), Importe = importe, ClaveIdempotencia = Guid.NewGuid() };

        private static PreContabilidad Caja(IEnumerable<PreContabilidad> lineas, string empresa = "1")
            => lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE && l.Empresa.Trim() == empresa);

        // ================================================================ como la caja de Nesto

        [TestMethod]
        public async Task Cobrar_DosEfectosEnteros_UnPagoPorEfectoYLaCajaDelRepartidorAlDebe()
        {
            Efecto(1, 50m, efecto: "1");
            Efecto(2, 30m, efecto: "2");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(80m, 1, 2), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(4321, r.Contenido.Asiento);
            Assert.AreEqual(0m, r.Contenido.ImporteACuenta);
            Assert.AreEqual(2, r.Contenido.Efectos);
            Assert.AreEqual(CAJA_MIGUEL, r.Contenido.Caja);
            List<PreContabilidad> clientes = registradas.Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE).ToList();
            CollectionAssert.AreEqual(new int?[] { 1, 2 }, clientes.Select(l => l.Liquidado).ToList());
            CollectionAssert.AreEqual(new[] { 50m, 30m }, clientes.Select(l => l.Haber).ToList());
            Assert.IsTrue(clientes.All(l => l.FormaPago == Constantes.FormasPago.EFECTIVO && l.Diario == "_Caja" && l.Nº_Cuenta == "15191"));
            Assert.AreEqual("S/Pago Factura NV26001/1 a Miguel", clientes[0].Concepto);
            PreContabilidad caja = Caja(registradas);
            Assert.AreEqual(CAJA_MIGUEL, caja.Nº_Cuenta);
            Assert.AreEqual(80m, caja.Debe);
            Assert.AreEqual("Pago c/15191/0 Factura NV26001/1 y otros", caja.Concepto);
            Assert.AreEqual(registradas.Sum(l => l.Debe), registradas.Sum(l => l.Haber), "El asiento cuadra");
        }

        [TestMethod]
        public async Task Cobrar_MenosQueLoMarcado_ElUltimoQuedaACuenta()
        {
            Efecto(1, 50m, efecto: "1");
            Efecto(2, 30m, efecto: "2");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(60m, 1, 2), MIGUEL);

            List<PreContabilidad> clientes = registradas.Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE).ToList();
            CollectionAssert.AreEqual(new[] { 50m, 10m }, clientes.Select(l => l.Haber).ToList());
            StringAssert.Contains(clientes[1].Concepto, "a cta.");
            Assert.AreEqual(0m, r.Contenido.ImporteACuenta);
            Assert.AreEqual(60m, Caja(registradas).Debe);
        }

        [TestMethod]
        public async Task Cobrar_MasQueLoMarcado_LoQueSobraVaACuentaDelCliente()
        {
            Efecto(1, 50m);

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(70m, 1), MIGUEL);

            Assert.AreEqual(20m, r.Contenido.ImporteACuenta);
            PreContabilidad aCuenta = registradas.Single(l => l.Nº_Documento == "A CUENTA" && l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE);
            Assert.AreEqual(20m, aCuenta.Haber);
            Assert.AreEqual("S/Pago a cuenta a Miguel", aCuenta.Concepto);
            Assert.AreEqual(70m, Caja(registradas).Debe);
        }

        [TestMethod]
        public async Task Cobrar_SinEfectos_TodoACuenta()
        {
            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(new CobroDeudaRepartoDTO
            {
                Cliente = "15191", Contacto = "2", Importe = 25m, ClaveIdempotencia = Guid.NewGuid()
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(25m, r.Contenido.ImporteACuenta);
            Assert.AreEqual(0, r.Contenido.Efectos);
            Assert.AreEqual(2, registradas.Count);
            Assert.AreEqual("2", registradas[0].Contacto);
            Assert.AreEqual("Pago c/15191/2 a cuenta a Miguel", Caja(registradas).Concepto);
            Assert.AreEqual(25m, Caja(registradas).Debe);
        }

        [TestMethod]
        public async Task Cobrar_ConUnAbono_ElAbonoVaPrimeroYSumaALoQueSeReparte()
        {
            // ELMAH 05/10/26 (Reina, cliente 26552): si el abono iba detrás, el reparto paraba antes y no cuadraba
            Efecto(1, 100m, efecto: "1");
            Efecto(2, -30m, documento: "NV26999", efecto: "1");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(70m, 1, 2), MIGUEL);

            List<PreContabilidad> clientes = registradas.Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE).ToList();
            Assert.AreEqual(2, clientes[0].Liquidado, "El abono, primero");
            Assert.AreEqual(30m, clientes[0].Debe);
            StringAssert.StartsWith(clientes[0].Concepto, "Devolución");
            Assert.AreEqual(100m, clientes[1].Haber);
            Assert.AreEqual(0m, r.Contenido.ImporteACuenta);
            Assert.AreEqual(70m, Caja(registradas).Debe);
        }

        [TestMethod]
        public async Task Cobrar_EfectosDeDosEmpresas_UnaContrapartidaPorEmpresaConLoSuyo()
        {
            // Nesto#514: llevaban todas el total cobrado y descuadraban las dos
            Efecto(1, 40m, empresa: "1  ");
            Efecto(2, 60m, empresa: "3  ");

            await servicio.Cobrar(Peticion(100m, 1, 2), MIGUEL);

            Assert.AreEqual(40m, Caja(registradas, "1").Debe);
            Assert.AreEqual(60m, Caja(registradas, "3").Debe);
        }

        [TestMethod]
        public async Task Cobrar_Impagado_ElConceptoLoDice()
        {
            Efecto(1, 50m, tipo: Constantes.ExtractosCliente.TiposApunte.IMPAGADO);

            await servicio.Cobrar(Peticion(50m, 1), MIGUEL);

            StringAssert.StartsWith(registradas[0].Concepto, "S/Pago Impagado");
        }

        [TestMethod]
        public async Task Cobrar_LaFechaEsLaDelAparato_NuncaDespuesDeHoy()
        {
            Efecto(1, 50m);
            CobroDeudaRepartoDTO ayer = Peticion(50m, 1);
            ayer.FechaHora = AHORA.AddDays(-1).AddHours(3);

            await servicio.Cobrar(ayer, MIGUEL);
            Assert.IsTrue(registradas.All(l => l.Fecha == AHORA.Date.AddDays(-1)), "Cobrado ayer sin cobertura: el asiento, de ayer");

            CobroDeudaRepartoDTO futuro = Peticion(50m, 1);
            futuro.FechaHora = AHORA.AddDays(2);
            await servicio.Cobrar(futuro, MIGUEL);
            Assert.IsTrue(registradas.All(l => l.Fecha == AHORA.Date));
        }

        // ================================================================ lo que esperaba en la cola

        [TestMethod]
        public async Task Cobrar_EfectoQueYaNoEstaPendiente_NoFallaYLoSuyoQuedaACuenta()
        {
            // Lo cobró otro mientras el cobro esperaba en la cola: el dinero está en la caja
            Efecto(1, 50m);
            Efecto(2, 30m).ImportePendiente = 0;

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(80m, 1, 2), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, r.Contenido.Efectos);
            Assert.AreEqual(30m, r.Contenido.ImporteACuenta);
            Assert.IsNotNull(r.Contenido.Aviso);
            Assert.AreEqual(80m, Caja(registradas).Debe);
        }

        [TestMethod]
        public async Task Cobrar_MismaClave_DevuelveLoYaCobradoSinContabilizarOtraVez()
        {
            var previo = new ResultadoCobroDeudaDTO { Asiento = 99, Importe = 80m };
            CobroDeudaRepartoDTO peticion = Peticion(80m);
            A.CallTo(() => repositorio.LeerPorClave(peticion.ClaveIdempotencia)).Returns(Task.FromResult(previo));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(peticion, MIGUEL);

            Assert.AreSame(previo, r.Contenido);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cobrar_DosALaVezConLaMismaClave_DevuelveLaDeLaOtra()
        {
            CobroDeudaRepartoDTO peticion = Peticion(10m);
            var otra = new ResultadoCobroDeudaDTO { Asiento = 77 };
            A.CallTo(() => repositorio.LeerPorClave(peticion.ClaveIdempotencia)).ReturnsNextFromSequence(
                Task.FromResult<ResultadoCobroDeudaDTO>(null), Task.FromResult(otra));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._))
                .Throws(new ClaveIdempotenciaRepetidaException(null));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(peticion, MIGUEL);

            Assert.AreEqual(77, r.Contenido.Asiento);
        }

        // ================================================================ rechazos

        [TestMethod]
        public async Task Cobrar_SinCajaPropia_400ConElMotivoYNoContabiliza()
        {
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", MIGUEL, "CajaDefecto")).Returns(null);

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(10m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            Assert.AreEqual(ServicioCobrosReparto.MENSAJE_SIN_CAJA, r.Mensaje);
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, "CajaDefecto")).MustNotHaveHappened();
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cobrar_UsuarioConDominio_LaCajaEsLaDeSuFilaSinDominio()
        {
            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(10m), @"NUEVAVISION\Miguel");

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(CAJA_MIGUEL, Caja(registradas).Nº_Cuenta);
        }

        [TestMethod]
        public async Task Cobrar_ImporteCeroONegativo_400()
        {
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cobrar(Peticion(0m), MIGUEL)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cobrar(Peticion(-5m), MIGUEL)).Tipo);
        }

        [TestMethod]
        public async Task Cobrar_SinClienteOSinClave_400()
        {
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cobrar(new CobroDeudaRepartoDTO { Importe = 5m, ClaveIdempotencia = Guid.NewGuid() }, MIGUEL)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cobrar(new CobroDeudaRepartoDTO { Cliente = "15191", Importe = 5m }, MIGUEL)).Tipo);
        }

        [TestMethod]
        public async Task Cobrar_EfectoDeOtroCliente_400()
        {
            Efecto(1, 50m, cliente: "99999");

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(Peticion(50m, 1), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "99999");
        }

        // ================================================================ controlador

        [TestMethod]
        public async Task Controlador_SinGrupoDeReparto403_ConAlmacenCobra()
        {
            var sinGrupo = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, MIGUEL) }, "prueba"));
            var almacen = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, MIGUEL), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
            }, "prueba"));

            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(sinGrupo, Peticion(10m)));
            Assert.AreEqual(HttpStatusCode.OK, await Estado(almacen, Peticion(10m)));
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        private async Task<HttpStatusCode> Estado(ClaimsPrincipal usuario, CobroDeudaRepartoDTO peticion)
        {
            var controlador = new RepartoPropioController(null, null)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario,
                ServicioCobros = servicio
            };
            return (await (await controlador.Cobrar(peticion)).ExecuteAsync(CancellationToken.None)).StatusCode;
        }
    }
}
