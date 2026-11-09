using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Informes;
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
    /// NestoAPI#802 (Ariadna#85): el cierre de caja del repartidor (POST api/RepartoPropio/Caja/Cierre) y MisCobros por empresa.
    /// Contrato cerrado por Carlos el 04/11/26: solo el importe, sin arqueo; el asiento, el de Nesto → Cajas → Traspasar de las tiendas.
    /// </summary>
    [TestClass]
    public class CierreCajaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 4, 19, 30, 0);
        private const string MIGUEL = @"NUEVAVISION\Miguel";
        private const string CAJA_MIGUEL = "57000032";

        private IRepositorioCierresCaja repositorio;
        private ILectorParametrosUsuario parametros;
        private IInformesService informes;
        private ServicioCierreCajaReparto servicio;
        private List<PreContabilidad> registradas;
        private AuditoriaCierreCaja auditoriaRegistrada;

        [TestInitialize]
        public void Setup()
        {
            registradas = null;
            auditoriaRegistrada = null;
            repositorio = A.Fake<IRepositorioCierresCaja>();
            A.CallTo(() => repositorio.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCierreCajaDTO>(null));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._))
                .ReturnsLazily((Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCierreCajaDTO r) =>
                {
                    registradas = lineas;
                    auditoriaRegistrada = r as AuditoriaCierreCaja;
                    int asiento = 5000;
                    return Task.FromResult(lineas.Select(l => new AsientoCierreCajaDTO
                    {
                        Empresa = l.Empresa.Trim(), Asiento = asiento++, Importe = l.Haber - l.Debe
                    }).ToList());
                });
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "Miguel", "CajaDefecto")).Returns(CAJA_MIGUEL);
            A.CallTo(() => parametros.LeerParametro("1", "Miguel", "DelegaciónDefecto")).Returns("ALG ");
            A.CallTo(() => parametros.LeerParametro("1", "Miguel", "FormaVentaDefecto")).Returns("VAR ");
            informes = A.Fake<IInformesService>();
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).Returns(0m);
            A.CallTo(() => informes.LeerExtractoContableAsync(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily(() => Task.FromResult(new List<ExtractoContableDTO>()));
            servicio = new ServicioCierreCajaReparto(repositorio, parametros, () => informes, () => AHORA);
        }

        /// <summary>El saldo de la caja en la empresa: el anterior al día más lo del día (como MisCobros).</summary>
        private void Saldo(string empresa, decimal anterior, decimal delDia = 0m, DateTime? dia = null)
        {
            DateTime fecha = (dia ?? AHORA).Date;
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(empresa, CAJA_MIGUEL, fecha)).Returns(anterior);
            A.CallTo(() => informes.LeerExtractoContableAsync(empresa, CAJA_MIGUEL, fecha, fecha)).ReturnsLazily(() => Task.FromResult(
                delDia == 0m ? new List<ExtractoContableDTO>() : new List<ExtractoContableDTO>
                {
                    new ExtractoContableDTO { Id = 1, Fecha = fecha, Concepto = "Reembolso", Debe = delDia, Haber = 0m, Asiento = 900 }
                }));
        }

        private static CierreCajaRepartoDTO Peticion(decimal importe, DateTime? fechaHora = null)
            => new CierreCajaRepartoDTO { Importe = importe, ClaveIdempotencia = Guid.NewGuid(), FechaHora = fechaHora };

        private static List<SaldoCajaEmpresa> Saldos(decimal empresa1, decimal empresa3)
            => new List<SaldoCajaEmpresa>
            {
                new SaldoCajaEmpresa { Empresa = "1", Saldo = empresa1 },
                new SaldoCajaEmpresa { Empresa = "3", Saldo = empresa3 }
            };

        // ================================================================ regla pura

        [TestMethod]
        public void Regla_TodoACero_NadaQueCerrar400()
        {
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(0m, 0m), 0m);

            Assert.AreEqual(TipoDecisionCierreCaja.NadaQueCerrar, d.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, d.TipoResultado);
            Assert.AreEqual("No hay nada que cerrar: la caja está a cero", d.Mensaje);
        }

        [TestMethod]
        public void Regla_TodoACero_AunqueDigaQueLlevaAlgo_NadaQueCerrar()
        {
            Assert.AreEqual(TipoDecisionCierreCaja.NadaQueCerrar, ReglaCierreCaja.Decidir(Saldos(0m, 0m), 50m).Tipo);
            Assert.AreEqual(TipoDecisionCierreCaja.NadaQueCerrar, ReglaCierreCaja.Decidir(Saldos(0.004m, -0.004m), 0m).Tipo,
                "Con dos decimales es cero");
        }

        [TestMethod]
        public void Regla_UnaEmpresaEnNegativo_409ConLaEmpresaYElImporte()
        {
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(100m, -12.5m), 87.5m);

            Assert.AreEqual(TipoDecisionCierreCaja.Negativo, d.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, d.TipoResultado);
            Assert.AreEqual("La caja de la empresa 3 está en negativo (−12,50 €): véalo con Administración antes de cerrar", d.Mensaje);
        }

        [TestMethod]
        public void Regla_NegativoManda_AunqueElTotalCuadre()
        {
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(-1234.56m, 2000m), 765.44m);

            Assert.AreEqual(TipoDecisionCierreCaja.Negativo, d.Tipo);
            StringAssert.Contains(d.Mensaje, "empresa 1 está en negativo (−1.234,56 €)");
        }

        [TestMethod]
        public void Regla_NoCuadra_409ConLoQueLlevaLoQueDiceYCadaEmpresa()
        {
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(1234.56m, 0m), 1200m);

            Assert.AreEqual(TipoDecisionCierreCaja.NoCuadra, d.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, d.TipoResultado);
            Assert.AreEqual("No cuadra: ha contado 1.200,00 € y su caja tiene 1.234,56 € (empresa 1: 1.234,56 €, empresa 3: 0,00 €).", d.Mensaje);
        }

        [TestMethod]
        public void Regla_NoCuadra_ConLasDosEmpresas()
        {
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(80m, 20.5m), 100m);

            Assert.AreEqual(TipoDecisionCierreCaja.NoCuadra, d.Tipo);
            Assert.AreEqual("No cuadra: ha contado 100,00 € y su caja tiene 100,50 € (empresa 1: 80,00 €, empresa 3: 20,50 €).", d.Mensaje);
        }

        [TestMethod]
        public void Regla_Cuadra_SoloEmpresa1_YConLasDos_YConDosDecimales()
        {
            Assert.AreEqual(TipoDecisionCierreCaja.Cuadra, ReglaCierreCaja.Decidir(Saldos(150m, 0m), 150m).Tipo);
            Assert.AreEqual(TipoDecisionCierreCaja.Cuadra, ReglaCierreCaja.Decidir(Saldos(80m, 20.5m), 100.5m).Tipo);
            Assert.AreEqual(TipoDecisionCierreCaja.Cuadra, ReglaCierreCaja.Decidir(Saldos(100m, 0m), 100.004m).Tipo, "Importe a dos decimales");
            DecisionCierreCaja d = ReglaCierreCaja.Decidir(Saldos(0m, 35m), 35m);
            Assert.AreEqual(TipoDecisionCierreCaja.Cuadra, d.Tipo, "Solo la empresa 3");
            Assert.AreEqual(TipoResultadoReparto.Ok, d.TipoResultado);
            Assert.IsNull(d.Mensaje);
        }

        [TestMethod]
        public void Euros_FormatoEspanolConDosDecimales()
        {
            Assert.AreEqual("1.234,56 €", ReglaCierreCaja.Euros(1234.56m));
            Assert.AreEqual("1.234,50 €", ReglaCierreCaja.Euros(1234.5m));
            Assert.AreEqual("0,00 €", ReglaCierreCaja.Euros(0m));
            Assert.AreEqual("12,35 €", ReglaCierreCaja.Euros(12.345m), "AwayFromZero");
            Assert.AreEqual("1.234.567,89 €", ReglaCierreCaja.Euros(1234567.891m));
            Assert.AreEqual("999,00 €", ReglaCierreCaja.Euros(999m));
        }

        // ================================================================ servicio

        [TestMethod]
        public async Task Cerrar_SinCaja_400ElMensajeDeSiempre()
        {
            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(10m), @"NUEVAVISION\SinCaja");

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.MENSAJE_SIN_CAJA, r.Mensaje);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_SinPeticionOSinClave_400()
        {
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cerrar(null, MIGUEL)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cerrar(new CierreCajaRepartoDTO { Importe = 10m }, MIGUEL)).Tipo);
        }

        [TestMethod]
        public async Task Cerrar_CajaACero_400NadaQueCerrar()
        {
            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(0m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            Assert.AreEqual(ReglaCierreCaja.MENSAJE_NADA_QUE_CERRAR, r.Mensaje);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_EmpresaEnNegativo_409SinContabilizar()
        {
            Saldo("1", 50m);
            Saldo("3", -5m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(45m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            StringAssert.Contains(r.Mensaje, "empresa 3 está en negativo");
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_NoCuadra_409SinContabilizar()
        {
            Saldo("1", 100m, 85.40m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(180m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual("No cuadra: ha contado 180,00 € y su caja tiene 185,40 € (empresa 1: 185,40 €, empresa 3: 0,00 €). " +
                "Véalo con Administración antes de cerrar.", r.Mensaje, "Sin avisador (como antes de #844): que lo vea con Administración");
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_Cuadra_SoloEmpresa1_ElApunteDeNestoCampoACampo()
        {
            Saldo("1", 100m, 85.40m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(185.40m, AHORA.AddHours(-1)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, registradas.Count, "Una línea por empresa con saldo");
            PreContabilidad l = registradas.Single();
            Assert.AreEqual("1", l.Empresa);
            Assert.AreEqual(Constantes.TiposApunteExtracto.PASO_A_CARTERA, l.TipoApunte);
            Assert.AreEqual("0", l.TipoApunte, "Paso a cartera");
            Assert.AreEqual(Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE, l.TipoCuenta);
            Assert.AreEqual(CAJA_MIGUEL, l.Nº_Cuenta);
            Assert.AreEqual("Cierre de caja de Miguel", l.Concepto);
            Assert.AreEqual(0m, l.Debe);
            Assert.AreEqual(185.40m, l.Haber, "Haber = saldo de la empresa");
            Assert.AreEqual(AHORA.Date, l.Fecha);
            Assert.AreEqual(AHORA.Date, l.FechaVto);
            Assert.AreEqual("TRASP_CAJA", l.Nº_Documento);
            Assert.AreEqual(1, l.Asiento);
            Assert.AreEqual("_TraspCaja", l.Diario);
            Assert.AreEqual(Constantes.Contabilidad.Diarios.DIARIO_TRASPASOS_CAJA, l.Diario);
            Assert.AreEqual("ALG", l.Delegación);
            Assert.AreEqual("VAR", l.FormaVenta);
            Assert.AreEqual("57000016", l.Contrapartida);
            Assert.AreEqual(Constantes.Cuentas.CAJA_PENDIENTE_RECIBIR_TIENDAS, l.Contrapartida);
            Assert.AreEqual("1", l.Origen);
            Assert.AreEqual(MIGUEL, l.Usuario);
            Assert.AreEqual(AHORA, l.Fecha_Modificación);
            Assert.IsNull(l.FormaPago, "Nesto no la pone en el traspaso");

            ResultadoCierreCajaDTO c = r.Contenido;
            Assert.AreEqual(AHORA.Date, c.Fecha);
            Assert.AreEqual(CAJA_MIGUEL, c.Cuenta);
            Assert.AreEqual(185.40m, c.Importe);
            Assert.AreEqual(1, c.Asientos.Count);
            Assert.AreEqual("1", c.Asientos[0].Empresa);
            Assert.AreEqual(5000, c.Asientos[0].Asiento);
            Assert.AreEqual(185.40m, c.Asientos[0].Importe);
            Assert.IsNotInstanceOfType(c, typeof(AuditoriaCierreCaja), "La respuesta, solo lo del contrato");
        }

        [TestMethod]
        public async Task Cerrar_Cuadra_ConLasDosEmpresas_UnaLineaYUnAsientoPorEmpresa()
        {
            Saldo("1", 0m, 120m);
            Saldo("3", 30.25m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(150.25m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(2, registradas.Count);
            PreContabilidad uno = registradas[0];
            PreContabilidad tres = registradas[1];
            Assert.AreEqual("1", uno.Empresa);
            Assert.AreEqual(120m, uno.Haber);
            Assert.AreEqual("3", tres.Empresa);
            Assert.AreEqual(30.25m, tres.Haber);
            foreach (PreContabilidad l in registradas)
            {
                Assert.AreEqual(CAJA_MIGUEL, l.Nº_Cuenta);
                Assert.AreEqual("57000016", l.Contrapartida);
                Assert.AreEqual("TRASP_CAJA", l.Nº_Documento);
                Assert.AreEqual("_TraspCaja", l.Diario);
                Assert.AreEqual("1", l.Origen, "Origen 1 también en la espejo, como Nesto");
                Assert.AreEqual("ALG", l.Delegación, "La delegación del usuario en la empresa 1, como Nesto");
            }
            CollectionAssert.AreEqual(new[] { "1", "3" }, r.Contenido.Asientos.Select(a => a.Empresa).ToArray());
            CollectionAssert.AreEqual(new[] { 120m, 30.25m }, r.Contenido.Asientos.Select(a => a.Importe).ToArray());
            Assert.AreEqual(150.25m, r.Contenido.Importe);
            Assert.AreEqual(MIGUEL, auditoriaRegistrada.Usuario, "El usuario va en Modificaciones.Nuevo");
            Assert.AreEqual(2, auditoriaRegistrada.Saldos.Count);
        }

        [TestMethod]
        public async Task Cerrar_Cuadra_SoloEmpresa3_UnaSolaLinea()
        {
            Saldo("3", 40m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(40m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual("3", registradas.Single().Empresa);
            Assert.AreEqual("3", r.Contenido.Asientos.Single().Empresa);
        }

        [TestMethod]
        public async Task Cerrar_UsuarioDeNombreLargo_ConceptoHasta50()
        {
            string largo = @"NUEVAVISION\" + new string('x', 60);
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", new string('x', 60), "CajaDefecto")).Returns(CAJA_MIGUEL);
            Saldo("1", 10m);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(10m), largo);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(50, registradas.Single().Concepto.Length);
            StringAssert.StartsWith(registradas.Single().Concepto, "Cierre de caja de xxx");
        }

        [TestMethod]
        public async Task Cerrar_FechaHoraDeAyer_SaldoYAsientoDeAyer_FuturaHoy()
        {
            DateTime ayer = AHORA.Date.AddDays(-1);
            Saldo("1", 25m, dia: ayer);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(25m, ayer.AddHours(20)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(ayer, registradas.Single().Fecha);
            Assert.AreEqual(ayer, r.Contenido.Fecha);

            Saldo("1", 60m);
            ResultadoReparto<ResultadoCierreCajaDTO> futura = await servicio.Cerrar(Peticion(60m, AHORA.AddDays(3)), MIGUEL);
            Assert.AreEqual(TipoResultadoReparto.Ok, futura.Tipo, futura.Mensaje);
            Assert.AreEqual(AHORA.Date, futura.Contenido.Fecha, "Nunca después de hoy");
        }

        [TestMethod]
        public async Task Cerrar_MismaClave_LaMismaRespuestaSinVolverAContabilizar()
        {
            var guardado = new ResultadoCierreCajaDTO
            {
                Fecha = AHORA.Date, Cuenta = CAJA_MIGUEL, Importe = 185.40m,
                Asientos = new List<AsientoCierreCajaDTO> { new AsientoCierreCajaDTO { Empresa = "1", Asiento = 4321, Importe = 185.40m } }
            };
            CierreCajaRepartoDTO p = Peticion(185.40m);
            A.CallTo(() => repositorio.LeerPorClave(p.ClaveIdempotencia)).Returns(guardado);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(p, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.AreSame(guardado, r.Contenido);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._)).MustNotHaveHappened();
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_DosALaVezConLaMismaClave_LaQuePierdeDevuelveLoDeLaOtra()
        {
            Saldo("1", 10m);
            CierreCajaRepartoDTO p = Peticion(10m);
            var deLaOtra = new ResultadoCierreCajaDTO { Fecha = AHORA.Date, Cuenta = CAJA_MIGUEL, Importe = 10m };
            A.CallTo(() => repositorio.LeerPorClave(p.ClaveIdempotencia)).ReturnsNextFromSequence(null, deLaOtra);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._))
                .Throws(new ClaveIdempotenciaRepetidaException(null));

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(p, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.AreSame(deLaOtra, r.Contenido);
        }

        [TestMethod]
        public async Task Cerrar_FallaLaContabilizacion_400ConElMotivo()
        {
            Saldo("1", 10m);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._))
                .Throws(new NestoBusinessException("No se ha podido contabilizar el cierre de caja de la empresa 1."));

            ResultadoReparto<ResultadoCierreCajaDTO> r = await servicio.Cerrar(Peticion(10m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "No se ha podido contabilizar");
        }

        [TestMethod]
        public void Repositorio_DelTexto_SoloLoDelContrato()
        {
            var auditoria = new AuditoriaCierreCaja
            {
                Fecha = AHORA.Date, Cuenta = CAJA_MIGUEL, Importe = 10m, Usuario = MIGUEL,
                Asientos = new List<AsientoCierreCajaDTO> { new AsientoCierreCajaDTO { Empresa = "1", Asiento = 77, Importe = 10m } }
            };

            ResultadoCierreCajaDTO leido = RepositorioCierresCajaReparto.DelTexto(Newtonsoft.Json.JsonConvert.SerializeObject(auditoria));

            Assert.IsNotInstanceOfType(leido, typeof(AuditoriaCierreCaja));
            Assert.AreEqual(77, leido.Asientos.Single().Asiento);
            Assert.AreEqual(10m, leido.Importe);
            Assert.AreEqual(CAJA_MIGUEL, leido.Cuenta);
        }

        // ================================================================ MisCobros por empresa

        private ServicioRepartoPropio ServicioMisCobros()
            => new ServicioRepartoPropio(A.Fake<IRepositorioRepartoPropio>(), () => AHORA, null, parametros, null, () => informes);

        [TestMethod]
        public async Task MisCobros_ConLaEmpresa3_TotalesYDetallePorEmpresa()
        {
            DateTime dia = AHORA.Date;
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("1", CAJA_MIGUEL, dia)).Returns(100m);
            A.CallTo(() => informes.LeerExtractoContableAsync("1", CAJA_MIGUEL, dia, dia)).ReturnsLazily(() => Task.FromResult(new List<ExtractoContableDTO>
            {
                new ExtractoContableDTO { Id = 1, Fecha = dia, Concepto = "Reembolso", Debe = 85.40m, Asiento = 901 }
            }));
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("3", CAJA_MIGUEL, dia)).Returns(10m);
            A.CallTo(() => informes.LeerExtractoContableAsync("3", CAJA_MIGUEL, dia, dia)).ReturnsLazily(() => Task.FromResult(new List<ExtractoContableDTO>
            {
                new ExtractoContableDTO { Id = 5, Fecha = dia, Concepto = "Cobro espejo", Debe = 20m, Asiento = 77 }
            }));

            ResultadoReparto<MisCobrosRepartoDTO> r = await ServicioMisCobros().MisCobros(null, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            MisCobrosRepartoDTO c = r.Contenido;
            Assert.AreEqual(110m, c.SaldoAnterior, "Totales");
            Assert.AreEqual(105.40m, c.TotalDia);
            Assert.AreEqual(215.40m, c.SaldoFinal);
            Assert.AreEqual(2, c.Apuntes.Count);
            CollectionAssert.AreEqual(new[] { "1", "3" }, c.Apuntes.Select(a => a.Empresa).ToArray());
            Assert.AreEqual(2, c.Empresas.Count);
            CobrosEmpresaRepartoDTO tres = c.Empresas.Single(e => e.Empresa == "3");
            Assert.AreEqual(10m, tres.SaldoAnterior);
            Assert.AreEqual(20m, tres.TotalDia);
            Assert.AreEqual(30m, tres.SaldoFinal);
            Assert.AreEqual("3", tres.Apuntes.Single().Empresa);
            Assert.AreEqual(185.40m, c.Empresas.Single(e => e.Empresa == "1").SaldoFinal);
        }

        [TestMethod]
        public async Task MisCobros_SinNadaEnLa3_SoloLaEmpresa1()
        {
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("1", CAJA_MIGUEL, AHORA.Date)).Returns(0m);

            MisCobrosRepartoDTO c = (await ServicioMisCobros().MisCobros(null, MIGUEL)).Contenido;

            Assert.AreEqual(1, c.Empresas.Count, "Siempre la 1, aunque esté a cero");
            Assert.AreEqual("1", c.Empresas[0].Empresa);
            Assert.AreEqual(0m, c.SaldoFinal);
        }

        [TestMethod]
        public async Task MisCobros_La3ConSaldoPeroSinApuntes_Sale()
        {
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("3", CAJA_MIGUEL, AHORA.Date)).Returns(15m);

            MisCobrosRepartoDTO c = (await ServicioMisCobros().MisCobros(null, MIGUEL)).Contenido;

            CollectionAssert.AreEqual(new[] { "1", "3" }, c.Empresas.Select(e => e.Empresa).ToArray());
            Assert.AreEqual(15m, c.SaldoFinal);
        }

        // ================================================================ controlador

        [TestMethod]
        public async Task Controlador_SinGrupoDeReparto403_ConAlmacenCierra200()
        {
            Saldo("1", 10m);

            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(Usuario(null), Peticion(10m)));
            Assert.AreEqual(HttpStatusCode.OK, await Estado(Usuario(Constantes.GruposSeguridad.ALMACEN), Peticion(10m)));
        }

        [TestMethod]
        public async Task Controlador_SinCaja400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, await Estado(Usuario(Constantes.GruposSeguridad.ALMACEN, "SinCaja"), Peticion(10m)));
        }

        [TestMethod]
        public async Task Controlador_NadaQueCerrar400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, await Estado(Usuario(Constantes.GruposSeguridad.ALMACEN), Peticion(0m)));
        }

        [TestMethod]
        public async Task Controlador_NoCuadra409_YNegativo409()
        {
            Saldo("1", 10m);
            Assert.AreEqual(HttpStatusCode.Conflict, await Estado(Usuario(Constantes.GruposSeguridad.ALMACEN), Peticion(9m)));

            Saldo("3", -1m);
            Assert.AreEqual(HttpStatusCode.Conflict, await Estado(Usuario(Constantes.GruposSeguridad.ALMACEN), Peticion(9m)));
        }

        private static ClaimsPrincipal Usuario(string grupo, string nombre = "Miguel")
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            if (grupo != null)
            {
                claims.Add(new Claim(ClaimTypes.Role, grupo));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private async Task<HttpStatusCode> Estado(ClaimsPrincipal usuario, CierreCajaRepartoDTO peticion)
        {
            var controlador = new RepartoPropioController(null, null)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario,
                ServicioCierreCaja = servicio
            };
            return (await (await controlador.CerrarCaja(peticion)).ExecuteAsync(CancellationToken.None)).StatusCode;
        }
    }
}
