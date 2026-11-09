using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Ariadna;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Concurrent;
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
    /// NestoAPI#844 (Carlos 09/11/26): el cierre de caja del repartidor deja de ser opcional. No se termina la jornada con dinero en la
    /// caja, no se empieza el día siguiente (cargar, recoger, cobrar) con dinero de días anteriores y un cierre que no cuadra avisa
    /// URGENTE a Administración. La furgoneta (Cargar, Recoger, Terminar jornada) está en <see cref="FurgonetaBultosTests"/>.
    /// </summary>
    [TestClass]
    public class CajaSinCerrarRepartoTests
    {
        /// <summary>Martes por la mañana: lo de ayer (lunes) es «de días anteriores».</summary>
        private static readonly DateTime AHORA = new DateTime(2026, 11, 10, 8, 30, 0);
        private const string MIGUEL = @"NUEVAVISION\Miguel";
        private const string CAJA_MIGUEL = "57000032";

        private ILectorParametrosUsuario parametros;
        private IInformesService informes;
        private ConcurrentDictionary<string, DateTime> limpias;
        private List<Exception> trazas;
        private DateTime ahora;
        private ControlCajaReparto control;

        [TestInitialize]
        public void Preparar()
        {
            ahora = AHORA;
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "Miguel", "CajaDefecto")).Returns(CAJA_MIGUEL);
            A.CallTo(() => parametros.LeerParametro("1", "Miguel", "DiarioCaja")).Returns("_Caja");
            informes = A.Fake<IInformesService>();
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).Returns(0m);
            A.CallTo(() => informes.LeerExtractoContableAsync(A<string>._, A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily(() => Task.FromResult(new List<ExtractoContableDTO>()));
            limpias = new ConcurrentDictionary<string, DateTime>();
            trazas = new List<Exception>();
            control = new ControlCajaReparto(parametros, () => informes, () => ahora, limpias, trazas.Add);
            VersionesAriadna.Vaciar();
        }

        [TestCleanup]
        public void Limpiar() => VersionesAriadna.Vaciar();

        /// <summary>La caja en la empresa: el saldo anterior a hoy y los apuntes de hoy (positivo entra, negativo sale).</summary>
        private void Caja(string empresa, decimal anterior, params decimal[] hoy)
        {
            DateTime dia = ahora.Date;
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(empresa, CAJA_MIGUEL, dia)).Returns(anterior);
            int id = 1;
            List<ExtractoContableDTO> lineas = hoy.Select(i => new ExtractoContableDTO
            {
                Id = id++, Fecha = dia, Concepto = i > 0 ? "Reembolso" : "Traspaso", Debe = i > 0 ? i : 0m, Haber = i < 0 ? -i : 0m, Asiento = 900 + id
            }).ToList();
            A.CallTo(() => informes.LeerExtractoContableAsync(empresa, CAJA_MIGUEL, dia, dia)).ReturnsLazily(() => Task.FromResult(lineas.ToList()));
        }

        private static CobrosEmpresaRepartoDTO Empresa(string empresa, decimal anterior, params decimal[] hoy)
            => new CobrosEmpresaRepartoDTO
            {
                Empresa = empresa,
                SaldoAnterior = anterior,
                Apuntes = hoy.Select(i => new ApunteCajaRepartoDTO { Importe = i, Empresa = empresa }).ToList(),
                TotalDia = hoy.Sum(),
                SaldoFinal = anterior + hoy.Sum()
            };

        // ================================================================ la regla (pura)

        [TestMethod]
        public void NoTerminar_CajaACero_Null()
        {
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 0m), Empresa("3", 0m) }));
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 50m, -50m) }), "Cerrada hoy: a cero");
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoTerminar(null));
        }

        [TestMethod]
        public void NoTerminar_SoloEmpresa1_LoQueLleva()
        {
            Assert.AreEqual("Antes de terminar, cierre la caja: lleva 85,40 €.",
                ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 0m, 85.40m), Empresa("3", 0m) }));
        }

        [TestMethod]
        public void NoTerminar_DosEmpresas_ConElDesglose()
        {
            Assert.AreEqual("Antes de terminar, cierre la caja: lleva 1.100,50 € (empresa 1: 1.080,00 €, empresa 3: 20,50 €).",
                ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 1000m, 80m), Empresa("3", 0m, 20.50m) }));
            Assert.AreEqual("Antes de terminar, cierre la caja: lleva 20,50 € (empresa 3: 20,50 €).",
                ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 0m), Empresa("3", 0m, 20.50m) }), "Solo la 3: se dice");
        }

        [TestMethod]
        public void NoTerminar_UnaEmpresaEnNegativo_NoBloquea()
        {
            // El cierre le diría «véalo con Administración»: dejarle sin terminar no lo arregla
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoTerminar(new[] { Empresa("1", 0m, 80m), Empresa("3", -5m) }));
        }

        [TestMethod]
        public void DeDiasAnteriores_LoCobradoHoyNoLoTapa_LoQueSaleHoySi()
        {
            Assert.AreEqual(120m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", 120m, 30m)), "Cobrar hoy no cierra lo de ayer");
            Assert.AreEqual(0m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", 120m, -120m, 30m)), "Administración lo traspasó hoy");
            Assert.AreEqual(70m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", 120m, -50m)), "Traspasado a medias");
            Assert.AreEqual(0m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", 0m, 30m)), "Solo lo de hoy");
            Assert.AreEqual(0m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", -10m)), "En negativo no es dinero que lleve");
            Assert.AreEqual(0m, ReglaCajaSinCerrar.DeDiasAnteriores(Empresa("1", 0.004m)), "Con dos decimales es cero");
        }

        [TestMethod]
        public void NoEmpezar_ElMensajeConLoDeDiasAnterioresDeLasDosEmpresas()
        {
            Assert.AreEqual("Tiene 105,90 € de días anteriores sin cerrar en su caja: ciérrela antes de empezar.",
                ReglaCajaSinCerrar.MotivoParaNoEmpezar(new[] { Empresa("1", 85.40m, 12m), Empresa("3", 20.50m) }));
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoEmpezar(new[] { Empresa("1", 0m, 12m) }));
            Assert.IsNull(ReglaCajaSinCerrar.MotivoParaNoEmpezar(new[] { Empresa("1", 85.40m), Empresa("3", -1m) }), "Con una en negativo, no");
        }

        [TestMethod]
        public void Conflicto_409ConSuCodigo()
        {
            ResultadoReparto<ResultadoCargaBultoDTO> r = ReglaCajaSinCerrar.Conflicto<ResultadoCargaBultoDTO>("Tiene…");

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual("CAJA_SIN_CERRAR", r.Codigo);
            Assert.AreEqual("Tiene…", r.Mensaje);
        }

        // ================================================================ el control (lee la caja del usuario)

        [TestMethod]
        public async Task Empezar_SinCaja_NoMiraNada()
        {
            Assert.IsNull(await control.MotivoParaNoEmpezar(@"NUEVAVISION\Andre", null));
            Assert.IsNull(await control.MotivoParaNoTerminar("Andre"));
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Empezar_ConDineroDeAyer_ElMotivo()
        {
            Caja("1", 85.40m);

            Assert.AreEqual("Tiene 85,40 € de días anteriores sin cerrar en su caja: ciérrela antes de empezar.",
                await control.MotivoParaNoEmpezar(MIGUEL, AHORA));
            Assert.IsNotNull(await control.MotivoParaNoEmpezar(MIGUEL, null), "Sigue bloqueado: no se recuerda una caja sucia");
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("1", CAJA_MIGUEL, AHORA.Date)).MustHaveHappened(2, Times.Exactly);
        }

        [TestMethod]
        public async Task Empezar_LoDeAyerQueLlegaTardePorLaCola_Pasa()
        {
            Caja("1", 85.40m);

            Assert.IsNull(await control.MotivoParaNoEmpezar(MIGUEL, AHORA.AddDays(-1).AddHours(10)));
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Empezar_Limpia_NoSeVuelveALeerEnTodoElDia_YMananaSi()
        {
            Caja("1", 0m, 40m);

            Assert.IsNull(await control.MotivoParaNoEmpezar(MIGUEL, null));
            Assert.IsNull(await control.MotivoParaNoEmpezar(MIGUEL, null));
            Assert.IsNull(await control.MotivoParaNoEmpezar(MIGUEL, null));
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync("1", CAJA_MIGUEL, AHORA.Date)).MustHaveHappenedOnceExactly();

            ahora = AHORA.AddDays(1);
            Caja("1", 40m);
            Assert.IsNotNull(await control.MotivoParaNoEmpezar(MIGUEL, null), "Al día siguiente, los 40 € de hoy sin cerrar");
        }

        [TestMethod]
        public async Task Empezar_SiNoSePuedeLeerLaCaja_SeLeDejaSeguirYQuedaEnElmah()
        {
            A.CallTo(() => informes.LeerSaldoContableAnteriorAsync(A<string>._, A<string>._, A<DateTime>._)).Throws(new InvalidOperationException("sin SQL"));

            Assert.IsNull(await control.MotivoParaNoEmpezar(MIGUEL, null));
            Assert.IsNull(await control.MotivoParaNoTerminar(MIGUEL));
            Assert.AreEqual(2, trazas.Count);
            StringAssert.Contains(trazas[0].Message, "[Cierre de caja #844]");
            StringAssert.Contains(trazas[0].Message, CAJA_MIGUEL);
        }

        [TestMethod]
        public async Task Terminar_ConLoDeHoyEnLaCaja_ElMotivo_YSinCacheo()
        {
            Caja("1", 0m, 60m, 25.40m);
            Caja("3", 0m, 20.50m);

            Assert.AreEqual("Antes de terminar, cierre la caja: lleva 105,90 € (empresa 1: 85,40 €, empresa 3: 20,50 €).",
                await control.MotivoParaNoTerminar(MIGUEL));

            Caja("1", 0m, 60m, 25.40m, -85.40m);
            Caja("3", 0m, 20.50m, -20.50m);
            Assert.IsNull(await control.MotivoParaNoTerminar(MIGUEL), "Cerrada: ya puede terminar");
        }

        // ================================================================ cobrar (#766)

        private static CobroDeudaRepartoDTO PeticionCobro(Guid? clave = null, DateTime? fechaHora = null)
            => new CobroDeudaRepartoDTO { Cliente = "15191", Importe = 30m, ClaveIdempotencia = clave ?? Guid.NewGuid(), FechaHora = fechaHora };

        private (ServicioCobrosReparto Servicio, IRepositorioCobrosReparto Repositorio) Cobros()
        {
            var repositorio = A.Fake<IRepositorioCobrosReparto>();
            A.CallTo(() => repositorio.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCobroDeudaDTO>(null));
            A.CallTo(() => repositorio.LeerEfectos(A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult(new List<EfectoCobroReparto>()));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._)).Returns(Task.FromResult(4321));
            return (new ServicioCobrosReparto(repositorio, parametros, () => ahora) { ControlCaja = control }, repositorio);
        }

        [TestMethod]
        public async Task Cobrar_ConDineroDeAyer_409SinCobrar()
        {
            Caja("1", 85.40m);
            (ServicioCobrosReparto servicio, IRepositorioCobrosReparto repositorio) = Cobros();

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(PeticionCobro(), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ReglaCajaSinCerrar.CODIGO, r.Codigo);
            Assert.AreEqual("Tiene 85,40 € de días anteriores sin cerrar en su caja: ciérrela antes de empezar.", r.Mensaje);
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cobrar_ElReintentoDeUnCobroYaHecho_DevuelveLoSuyoAunqueLaCajaEsteSucia()
        {
            Caja("1", 85.40m);
            (ServicioCobrosReparto servicio, IRepositorioCobrosReparto repositorio) = Cobros();
            Guid clave = Guid.NewGuid();
            A.CallTo(() => repositorio.LeerPorClave(clave)).Returns(Task.FromResult(new ResultadoCobroDeudaDTO { Asiento = 77, Importe = 30m }));

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(PeticionCobro(clave), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.AreEqual(77, r.Contenido.Asiento);
        }

        [TestMethod]
        public async Task Cobrar_ConLaCajaLimpia_ComoAntes()
        {
            Caja("1", 0m, 12m);
            (ServicioCobrosReparto servicio, _) = Cobros();

            ResultadoReparto<ResultadoCobroDeudaDTO> r = await servicio.Cobrar(PeticionCobro(), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(4321, r.Contenido.Asiento);
        }

        // ================================================================ el cierre que no cuadra

        private ServicioCierreCajaReparto Cierre(IAvisadorCierreCaja avisador, Func<string, string> version = null)
        {
            var repositorio = A.Fake<IRepositorioCierresCaja>();
            A.CallTo(() => repositorio.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCierreCajaDTO>(null));
            A.CallTo(() => repositorio.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCierreCajaDTO>._))
                .ReturnsLazily((Guid c, string u, List<PreContabilidad> lineas, ResultadoCierreCajaDTO r) => Task.FromResult(
                    lineas.Select(l => new AsientoCierreCajaDTO { Empresa = l.Empresa.Trim(), Asiento = 5000, Importe = l.Haber }).ToList()));
            return new ServicioCierreCajaReparto(repositorio, parametros, () => informes, () => ahora, avisador, version);
        }

        private static CierreCajaRepartoDTO PeticionCierre(decimal importe)
            => new CierreCajaRepartoDTO { Importe = importe, ClaveIdempotencia = Guid.NewGuid() };

        [TestMethod]
        public async Task Cierre_NoCuadra_AvisaAAdministracionYSeLoDiceAlRepartidor()
        {
            Caja("1", 0m, 185.40m);
            Caja("3", 0m, 20m);
            var avisador = A.Fake<IAvisadorCierreCaja>();
            DescuadreCierreCaja avisado = null;
            A.CallTo(() => avisador.Descuadre(A<DescuadreCierreCaja>._)).Invokes((DescuadreCierreCaja d) => avisado = d).Returns(Task.CompletedTask);

            ResultadoReparto<ResultadoCierreCajaDTO> r = await Cierre(avisador, u => u == MIGUEL ? "2.32" : null).Cerrar(PeticionCierre(200m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual("No cuadra: ha contado 200,00 € y su caja tiene 205,40 € (empresa 1: 185,40 €, empresa 3: 20,00 €). " +
                "Ya hemos avisado a Administración.", r.Mensaje);
            Assert.AreEqual(MIGUEL, avisado.Usuario);
            Assert.AreEqual(CAJA_MIGUEL, avisado.Caja);
            Assert.AreEqual(AHORA.Date, avisado.Fecha);
            Assert.AreEqual(200m, avisado.Contado);
            Assert.AreEqual(205.40m, avisado.Saldo);
            Assert.AreEqual(2, avisado.Saldos.Count);
            Assert.AreEqual("2.32", avisado.VersionAriadna);
            A.CallTo(() => avisador.Cerrada(A<string>._, A<DateTime>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cierre_EnNegativoOACero_NoAvisaDeDescuadre()
        {
            var avisador = A.Fake<IAvisadorCierreCaja>();
            Caja("1", 0m, 10m);
            Caja("3", -1m);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, (await Cierre(avisador).Cerrar(PeticionCierre(9m), MIGUEL)).Tipo);

            Caja("1", 0m);
            Caja("3", 0m);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await Cierre(avisador).Cerrar(PeticionCierre(9m), MIGUEL)).Tipo);

            A.CallTo(() => avisador.Descuadre(A<DescuadreCierreCaja>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cierre_Cuadra_CierraElAvisoEnNesto()
        {
            Caja("1", 0m, 185.40m);
            var avisador = A.Fake<IAvisadorCierreCaja>();

            ResultadoReparto<ResultadoCierreCajaDTO> r = await Cierre(avisador).Cerrar(PeticionCierre(185.40m), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            A.CallTo(() => avisador.Cerrada(CAJA_MIGUEL, AHORA.Date, MIGUEL)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Descuadre(A<DescuadreCierreCaja>._)).MustNotHaveHappened();
        }

        // ================================================================ el aviso urgente

        private static DescuadreCierreCaja Descuadre(string version = "2.32") => new DescuadreCierreCaja
        {
            Usuario = MIGUEL,
            Caja = CAJA_MIGUEL,
            Fecha = AHORA.Date,
            Contado = 200m,
            Saldo = 205.40m,
            Saldos = new List<SaldoCajaEmpresa> { new SaldoCajaEmpresa { Empresa = "1", Saldo = 185.40m }, new SaldoCajaEmpresa { Empresa = "3", Saldo = 20m } },
            VersionAriadna = version
        };

        [TestMethod]
        public void Aviso_EsUrgente_ConSuReferenciaYLosDatos()
        {
            NotificacionPushDTO n = TextosCierreCajaDescuadrado.Aviso(Descuadre());

            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(n), "En la lista blanca de #775");
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(new NotificacionPushDTO { Datos = n.Datos }), "Por Datos[\"tipo\"] (el buzón)");
            Assert.AreEqual("CierreCajaDescuadrado", n.Tipo);
            Assert.AreEqual("57000032|20261110", n.Datos["referencia"]);
            Assert.AreEqual("Miguel", n.Datos["repartidor"]);
            Assert.AreEqual("200.00", n.Datos["importe"]);
            Assert.AreEqual("205.40", n.Datos["saldo"]);
            Assert.AreEqual("Cierre de caja que no cuadra: Miguel ha contado 200,00 € y su caja tiene 205,40 €", n.Titulo);
            Assert.AreEqual("Miguel ha intentado cerrar su caja 57000032 con 200,00 €, pero la contabilidad dice 205,40 € " +
                "(empresa 1: 185,40 €, empresa 3: 20,00 €): le faltan 5,40 €. La caja NO se ha cerrado. Llámele y revísenlo juntos. (Ariadna 2.32)", n.Cuerpo);
        }

        [TestMethod]
        public void Aviso_SinVersion_NoLaDice_YLeSobran()
        {
            DescuadreCierreCaja d = Descuadre(null);
            d.Contado = 207.40m;

            NotificacionPushDTO n = TextosCierreCajaDescuadrado.Aviso(d);

            StringAssert.EndsWith(n.Cuerpo, "le sobran 2,00 €. La caja NO se ha cerrado. Llámele y revísenlo juntos.");
        }

        [TestMethod]
        public void Referencia_IdaYVuelta_YLasQueNoSon()
        {
            Assert.IsTrue(TextosCierreCajaDescuadrado.LeerReferencia("57000032|20261110", out string caja, out DateTime fecha));
            Assert.AreEqual(CAJA_MIGUEL, caja);
            Assert.AreEqual(AHORA.Date, fecha);
            Assert.IsFalse(TextosCierreCajaDescuadrado.LeerReferencia("12", out _, out _), "Una autorización de reembolso no es");
            Assert.IsFalse(TextosCierreCajaDescuadrado.LeerReferencia("57000032|ayer", out _, out _));
            Assert.IsFalse(TextosCierreCajaDescuadrado.LeerReferencia(null, out _, out _));
        }

        [TestMethod]
        public async Task Comprobador_PendienteMientrasTengaSaldoYNoSeHayaCerrado()
        {
            var estado = A.Fake<IEstadoCajaReparto>();
            A.CallTo(() => estado.Saldo("57000032")).Returns(205.40m);
            A.CallTo(() => estado.Saldo("57000099")).Returns(0m);
            A.CallTo(() => estado.Saldo("57000050")).Returns(10m);
            A.CallTo(() => estado.CerradaDesde("57000050", AHORA.Date)).Returns(true);
            var comprobador = new ComprobadorCierresCajaDescuadrados(estado);

            ISet<string> pendientes = await comprobador.Pendientes(new[] { "57000032|20261110", "57000099|20261110", "57000050|20261110", "basura" });

            Assert.AreEqual("CierreCajaDescuadrado", comprobador.Tipo);
            CollectionAssert.AreEquivalent(new[] { "57000032|20261110" }, pendientes.ToList(),
                "La 99 está a cero (Administración lo traspasó a mano); la 50 se cerró después");
        }

        [TestMethod]
        public async Task ServicioUrgentes_ElDescuadreSaleMientrasSigaPendiente()
        {
            var buzon = A.Fake<IRepositorioUrgentesBuzon>();
            NotificacionPushDTO aviso = TextosCierreCajaDescuadrado.Aviso(Descuadre());
            A.CallTo(() => buzon.Leer(A<string>._, A<DateTime>._)).Returns(Task.FromResult(new List<NotificacionBuzonDTO>
            {
                new NotificacionBuzonDTO { Id = 7, Titulo = aviso.Titulo, Cuerpo = aviso.Cuerpo, Datos = aviso.Datos, FechaCreacion = AHORA }
            }));
            var estado = A.Fake<IEstadoCajaReparto>();
            A.CallTo(() => estado.Saldo(CAJA_MIGUEL)).Returns(205.40m);
            var servicio = new ServicioNotificacionesUrgentes(buzon, new IComprobadorUrgentesPendientes[] { new ComprobadorCierresCajaDescuadrados(estado) },
                () => AHORA.AddHours(1));

            List<NotificacionUrgenteDTO> urgentes = await servicio.Pendientes(@"NUEVAVISION\Laura");

            Assert.AreEqual(1, urgentes.Count);
            Assert.AreEqual("CierreCajaDescuadrado", urgentes[0].Tipo);
            Assert.AreEqual("57000032|20261110", urgentes[0].Referencia);
        }

        [TestMethod]
        public async Task Avisador_AlBuzonDeNestoDeAdministracion_YElmah()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var guardados = new List<(string Usuario, string Aplicacion, NotificacionPushDTO Aviso)>();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string app, NotificacionPushDTO n) => guardados.Add((u, app, n))).Returns(Task.CompletedTask);
            var tiempoReal = A.Fake<IAvisosTiempoReal>();
            var avisador = new AvisadorCierreCaja(push, g => g == Constantes.GruposSeguridad.ADMINISTRACION
                ? new List<string> { "Laura", @"NUEVAVISION\Marta", "laura", "(Grupo)" } : new List<string> { "Carlos" }, tiempoReal, trazas.Add);

            await avisador.Descuadre(Descuadre());

            CollectionAssert.AreEqual(new[] { @"NUEVAVISION\Laura", @"NUEVAVISION\Marta" }, guardados.Select(g => g.Usuario).ToList());
            Assert.IsTrue(guardados.All(g => g.Aplicacion == Constantes.Aplicaciones.NESTO && g.Aviso.Tipo == "CierreCajaDescuadrado"));
            Assert.AreEqual(1, trazas.Count, "Queda en ELMAH");
            StringAssert.Contains(trazas[0].Message, "[Cierre de caja #844] Miguel ha intentado cerrar su caja 57000032");
        }

        [TestMethod]
        public async Task Avisador_SiFallaElBuzon_NoLanza()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Throws(new InvalidOperationException("sin BD"));
            var avisador = new AvisadorCierreCaja(push, g => new List<string> { "Laura" }, A.Fake<IAvisosTiempoReal>(), trazas.Add);

            await avisador.Descuadre(Descuadre());

            Assert.AreEqual(2, trazas.Count, "El descuadre y el fallo al avisar");
        }

        [TestMethod]
        public void Avisador_Cerrada_ResueltaParaHoyYAyer()
        {
            var tiempoReal = A.Fake<IAvisosTiempoReal>();
            var resueltas = new List<NotificacionResueltaDTO>();
            A.CallTo(() => tiempoReal.NotificacionResuelta(A<IEnumerable<string>>._, A<NotificacionResueltaDTO>._))
                .Invokes((IEnumerable<string> u, NotificacionResueltaDTO r) => resueltas.Add(r));
            var avisador = new AvisadorCierreCaja(A.Fake<IServicioNotificacionesPush>(), g => new List<string> { "Laura" }, tiempoReal, trazas.Add);

            avisador.Cerrada(CAJA_MIGUEL, AHORA.Date, MIGUEL);

            CollectionAssert.AreEqual(new[] { "57000032|20261110", "57000032|20261109" }, resueltas.Select(r => r.Referencia).ToList());
            Assert.IsTrue(resueltas.All(r => r.Tipo == "CierreCajaDescuadrado" && r.Respondio == "Miguel" && r.Resultado == "Cerrada"));
        }

        // ================================================================ la versión de Ariadna de cada usuario

        [TestMethod]
        public void Versiones_LaUltimaDeCadaUsuario_ConYSinDominio()
        {
            VersionesAriadna.Apuntar(MIGUEL, "2.30", "PDA-3", AHORA.AddDays(-1));
            VersionesAriadna.Apuntar("Miguel", " 2.31 ", null, AHORA);
            VersionesAriadna.Apuntar(null, "2.31", null, AHORA);
            VersionesAriadna.Apuntar("Andre", " ", null, AHORA);

            VersionAriadnaUsuarioDTO v = VersionesAriadna.De(MIGUEL);
            Assert.AreEqual("2.31", v.Version);
            Assert.AreEqual(AHORA, v.UltimaVez);
            Assert.IsNull(v.Dispositivo);
            Assert.AreEqual(1, VersionesAriadna.Todas().Count);
            Assert.IsNull(VersionesAriadna.De("Andre"));
        }

        [TestMethod]
        public async Task Versiones_ElHandlerApuntaLaDeLaCabecera_AunqueNoEsteDesactualizada()
        {
            var handler = new VersionMinimaAriadnaHandler(PoliticaVersionMinimaAriadna.Crear(null, null), _ => { }, () => AHORA.Date, () => AHORA)
            {
                InnerHandler = new Respuesta200()
            };
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/RepartoPropio/Envios");
            request.Headers.Add("X-Ariadna-Version", "2.31");
            request.Headers.Add(AvisosAlmacen.CABECERA_DISPOSITIVO, "PDA-3");
            request.SetRequestContext(new System.Web.Http.Controllers.HttpRequestContext
            {
                Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, MIGUEL) }, "prueba"))
            });

            HttpResponseMessage respuesta = await new HttpMessageInvoker(handler).SendAsync(request, CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("2.31", VersionesAriadna.De("Miguel")?.Version);
            Assert.AreEqual("PDA-3", VersionesAriadna.De("Miguel")?.Dispositivo);
        }

        private sealed class Respuesta200 : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
