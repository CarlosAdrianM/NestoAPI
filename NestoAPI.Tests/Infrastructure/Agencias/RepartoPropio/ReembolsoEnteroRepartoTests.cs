using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Carlos 19/11/26: «Si lleva reembolso debe ser entero o autorizado». En efectivo, cobrar menos del reembolso (más de 1 céntimo) exige la
    /// misma autorización que no cobrar (#773); sin ella, 409 REEMBOLSO_PARCIAL. Cobrar más, como antes.
    /// </summary>
    [TestClass]
    public class ReembolsoEnteroRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 19, 10, 15, 0);
        private const string USUARIO = @"NUEVAVISION\Repartidor";

        private ServicioRepartoPropioTests.RepositorioFalso reparto;
        private CobroTarjetaRepartoTests.AutorizacionesFalsas autorizacionesRepo;
        private IAvisadorAutorizacionesReembolso avisador;
        private ServicioAutorizacionesReembolso servicioAutorizaciones;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Preparar()
        {
            reparto = new ServicioRepartoPropioTests.RepositorioFalso();
            autorizacionesRepo = new CobroTarjetaRepartoTests.AutorizacionesFalsas();
            avisador = A.Fake<IAvisadorAutorizacionesReembolso>();
            servicioAutorizaciones = new ServicioAutorizacionesReembolso(reparto, autorizacionesRepo, avisador, () => AHORA, _ => { });
            var parametros = new ServicioRepartoPropioTests.ParametrosFalsos();
            parametros.Caja("Repartidor", "57000001");
            servicio = new ServicioRepartoPropio(reparto, () => AHORA, null, parametros, () => A.Fake<IServicioPagos>())
            {
                CobroEntrega = new ComprobadorCobroEntrega(new CobroTarjetaRepartoTests.TarjetasFalsas(), autorizacionesRepo, avisador, () => AHORA)
            };
            reparto.Con(ServicioRepartoPropioTests.Envio(reembolso: 120m));
        }

        private static EntregaRepartoDTO Efectivo(decimal? importe, string telefonica = null)
            => new EntregaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(),
                CobroReembolso = PruebaEntregaReparto.COBRO_EFECTIVO,
                ImporteCobrado = importe,
                AutorizacionTelefonica = telefonica
            };

        [TestMethod]
        public async Task EfectivoMenor_SinAutorizacion_409ReembolsoParcial_YNoSeRegistraNada()
        {
            var r = await servicio.Entregar(251300, Efectivo(100m), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ReglaReembolsoEntero.CODIGO, r.Codigo);
            StringAssert.StartsWith(r.Mensaje, "El reembolso es de 120,00 € y ha cobrado 100,00 €: hace falta autorización para cobrar menos.");
            Assert.AreEqual(0, reparto.Registros.Count);
        }

        [TestMethod]
        public async Task EfectivoEntero_OSinImporte_OUnCentimoMenos_PasaComoSiempre()
        {
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Efectivo(119.99m), USUARIO)).Tipo);
            Assert.AreEqual(119.99m, reparto.Registros.Single().ImporteAContabilizar);
            Assert.IsFalse(ReglaReembolsoEntero.EsParcial(120m, null));
            Assert.IsFalse(ReglaReembolsoEntero.EsParcial(120m, 120m));
            Assert.IsFalse(ReglaReembolsoEntero.EsParcial(120m, 150m), "Cobrar de más no es parcial (va por COBRO_DE_MAS)");
            Assert.IsTrue(ReglaReembolsoEntero.EsParcial(120m, 119.98m));
        }

        [TestMethod]
        public async Task EfectivoMenor_ConAutorizacionPedidaYAutorizada_SeEntregaYSeContabilizaLoCobrado()
        {
            var pedida = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = "Le falta dinero", ImporteCobrado = 100m }, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, pedida.Tipo, pedida.Mensaje);
            Assert.AreEqual("Cobra 100,00 € de 120,00 €. Le falta dinero", autorizacionesRepo.Filas.Single().Motivo);

            var pendiente = await servicio.Entregar(251300, Efectivo(100m), USUARIO);
            Assert.AreEqual(ReglaReembolsoEntero.CODIGO, pendiente.Codigo);
            StringAssert.Contains(pendiente.Mensaje, "pendiente de Administración");

            _ = await servicioAutorizaciones.Responder(pedida.Contenido.Id, new RespuestaAutorizacionReembolsoDTO { Autorizar = true }, @"NUEVAVISION\Laura");
            var r = await servicio.Entregar(251300, Efectivo(100m), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(100m, reparto.Registros.Single().ImporteAContabilizar);
        }

        [TestMethod]
        public async Task NotaDeCobro_CobrarMenos_NoPideAutorizacion_QuedaACuenta()
        {
            var comprobador = new ComprobadorCobroEntrega(new CobroTarjetaRepartoTests.TarjetasFalsas(), autorizacionesRepo, avisador, () => AHORA);
            EnviosAgencia envio = reparto.Envios[251300];

            ComprobacionCobroEntrega nc = await comprobador.Comprobar(envio, PruebaEntregaReparto.COBRO_EFECTIVO, Efectivo(40m), USUARIO, notaCobro: true);
            ComprobacionCobroEntrega normal = await comprobador.Comprobar(envio, PruebaEntregaReparto.COBRO_EFECTIVO, Efectivo(40m), USUARIO);

            Assert.IsNull(nc.Error);
            Assert.AreEqual(ReglaReembolsoEntero.CODIGO, normal.Codigo);
        }

        [TestMethod]
        public async Task Autorizacion_ElMotivoDeAriadnaYaTraeLoCobrado_NoSeRepite()
        {
            const string motivo = "Cobra 100,00 € en efectivo de un reembolso de 120,00 € (faltan 20,00 €). No tiene más";

            _ = await servicioAutorizaciones.Pedir(251300, new PeticionAutorizacionReembolsoDTO { Motivo = motivo, ImporteCobrado = 100m }, USUARIO);

            Assert.AreEqual(motivo, autorizacionesRepo.Filas.Single().Motivo);
        }

        [TestMethod]
        public async Task EfectivoMenor_ConAutorizacionTelefonica_SeEntrega()
        {
            var r = await servicio.Entregar(251300, Efectivo(100m, "Autoriza Pepe por teléfono"), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(EstadosAutorizacionReembolso.TELEFONICA, autorizacionesRepo.Filas.Single().Estado);
        }

        // ================================================================ Carlos 26/11/26: cobrar de más, con confirmación

        [TestMethod]
        public async Task EfectivoDeMas_SinConfirmar_409CobroDeMas_YNoSeRegistraNada()
        {
            var r = await servicio.Entregar(251300, Efectivo(150m), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ReglaReembolsoEntero.CODIGO_DE_MAS, r.Codigo);
            Assert.AreEqual("Está cobrando 30,00 € más que el reembolso (120,00 €). Si es correcto, confírmelo: lo de más quedará a cuenta del cliente.", r.Mensaje);
            Assert.AreEqual(0, reparto.Registros.Count);
        }

        [TestMethod]
        public async Task EfectivoDeMas_Confirmado_SeEntregaYSeContabilizaLoCobrado_YElReintentoNoSeFrena()
        {
            EntregaRepartoDTO peticion = Efectivo(150m);
            peticion.ConfirmarCobroDeMas = true;

            var r = await servicio.Entregar(251300, peticion, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(150m, reparto.Registros.Single().ImporteAContabilizar);

            // El reintento con la misma clave (aunque se pierda la confirmación) devuelve lo de antes
            peticion.ConfirmarCobroDeMas = null;
            var otraVez = await servicio.Entregar(251300, peticion, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, otraVez.Tipo, otraVez.Mensaje);
            Assert.AreEqual(1, reparto.Registros.Count);
        }

        [TestMethod]
        public void ReglaDeMas_UnCentimoDeTolerancia_NotasDeCobroYOtrosCobrosNo()
        {
            Assert.IsFalse(ReglaReembolsoEntero.EsDeMas(120m, 120.01m));
            Assert.IsTrue(ReglaReembolsoEntero.EsDeMas(120m, 120.02m));
            Assert.IsFalse(ReglaReembolsoEntero.EsDeMas(120m, null));
            Assert.IsFalse(ReglaReembolsoEntero.EsDeMas(0m, 10m), "Sin reembolso no hay «de más»");
            Assert.IsTrue(ReglaReembolsoEntero.PideConfirmarDeMas(true, false, 120m, 150m, null));
            Assert.IsFalse(ReglaReembolsoEntero.PideConfirmarDeMas(true, true, 120m, 150m, null), "Nota de cobro: no cambia");
            Assert.IsFalse(ReglaReembolsoEntero.PideConfirmarDeMas(false, false, 120m, 150m, null), "Enlace o tarjeta: no");
            Assert.IsFalse(ReglaReembolsoEntero.PideConfirmarDeMas(true, false, 120m, 150m, true));
        }
    }
}
