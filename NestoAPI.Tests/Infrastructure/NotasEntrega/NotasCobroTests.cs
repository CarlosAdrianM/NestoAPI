using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#786 (correo del 929609, 03/11/26): la nota de solo COBRO. Se crea con su parada NC{pedido} de la agencia 14 (Bultos 0,
    /// Reembolso = lo pendiente), sin líneas (no entra en picking, packing ni Facturar rutas), sale en el reparto como «NotaCobro» con
    /// sus efectos, no en la furgoneta, y al entregarla el cobro liquida esos efectos; sin cobro, hace falta autorización (#773).
    /// </summary>
    [TestClass]
    public class NotasCobroTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 4, 10, 0, 0);
        private const string CLIENTE = "15191";
        private const string MIGUEL = "Miguel";
        private const string CAJA_MIGUEL = "57000032";

        private NotasCobroFalsas repositorio;
        private ServicioNotasCobro servicio;

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            repositorio = new NotasCobroFalsas();
            repositorio.Fichas[CLIENTE + "/0"] = new FichaNotaCobro
            {
                Nombre = "PELUQUERÍA LA BONITA",
                Direccion = "Calle Mayor, 1",
                CodPostal = "28110",
                Poblacion = "ALGETE",
                Provincia = "MADRID",
                Telefonos = "916281914/600123456",
                Vendedor = "NV",
                Ruta = "AT",
                Iva = "G21"
            };
            servicio = new ServicioNotasCobro(repositorio, () => true, () => AHORA);
        }

        [TestCleanup]
        public void Limpiar() => RutasPropiasConfiguradas.Reiniciar();

        private static EfectoCobroReparto Efecto(int id, decimal pendiente, string documento = "NV26/001234", string empresa = "1  ",
            string cliente = CLIENTE, string estado = "NRM")
            => new EfectoCobroReparto
            {
                Id = id,
                Empresa = empresa,
                Cliente = cliente + "     ",
                Contacto = "0  ",
                TipoApunte = "2",
                Documento = documento + "  ",
                Efecto = "1",
                Importe = pendiente,
                ImportePendiente = pendiente,
                Vencimiento = new DateTime(2026, 10, 30),
                Delegacion = "ALG",
                FormaVenta = "TEL",
                Ruta = "AT",
                Vendedor = "NV",
                Estado = estado
            };

        // ================================================================ crear la nota

        [TestMethod]
        public async Task Crear_ConEfectos_ParadaNCDeLaAgencia14_SinBultos_YConReembolsoLoPendiente()
        {
            repositorio.Efectos.Add(Efecto(501, 100m));
            repositorio.Efectos.Add(Efecto(502, 34.16m, "NV26/001300"));

            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Efectos = new List<int> { 502, 501 } }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(134.16m, r.Contenido.Importe);
            Assert.AreEqual("NC" + NotasCobroFalsas.PEDIDO, r.Contenido.CodigoDocumento);
            Assert.AreEqual("0", r.Contenido.Contacto, "Sin contacto, el del primer efecto");
            CollectionAssert.AreEqual(new[] { 502, 501 }, repositorio.EfectosGuardados.ToArray(), "Los efectos, en el orden en que se liquidan");
            Assert.AreEqual(134.16m, repositorio.ImporteGuardado);

            FilaNotaEntregaCabecera fila = repositorio.FilaGuardada;
            Assert.AreEqual(TiposNotaEntrega.COBRO, fila.Tipo);
            Assert.AreEqual((short)0, fila.Bultos);

            CabPedidoVta cabecera = repositorio.CabeceraGuardada;
            Assert.IsTrue(cabecera.NotaEntrega);
            Assert.AreEqual(NotasCobroFalsas.PEDIDO, cabecera.Número);
            Assert.AreEqual("AT", cabecera.Ruta);
            Assert.AreEqual(Constantes.PlazosPago.CONTADO, cabecera.PlazosPago);
            Assert.IsNull(cabecera.CCC, "Nada que domiciliar");
            Assert.AreEqual(0, cabecera.LinPedidoVtas?.Count ?? 0, "Sin líneas: no entra en picking, packing ni Facturar rutas");

            EnviosAgencia envio = repositorio.EnvioGuardado;
            Assert.AreEqual(Constantes.Agencias.AGENCIA_REPARTO_PROPIO, envio.Agencia);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
            Assert.AreEqual((short)0, envio.Bultos, "Una nota de cobro no lleva bultos");
            Assert.AreEqual(134.16m, envio.Reembolso);
            Assert.AreEqual(AHORA.Date, envio.FechaPagoReembolso, "NestoAPI#778: lo cobra nuestro repartidor");
            Assert.AreEqual(NotasCobroFalsas.PEDIDO, envio.Pedido);
            Assert.AreEqual("Cobrar 134,16 €", envio.Observaciones);
            Assert.AreEqual(ServicioNotasCobro.HISTORIA_PARADA, repositorio.HistoriaGuardada);
            Assert.AreEqual(NotasCobroFalsas.ENVIO, r.Contenido.EnvioId);
            StringAssert.StartsWith(r.Contenido.Texto, "Cobro de 134,16 €\nFactura NV26/001300/1 (vto. 30/10/26): 34,16 €");
        }

        [TestMethod]
        public async Task Crear_SoloImporte_SinEfectos_YConFechaFutura()
        {
            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO
            {
                Cliente = CLIENTE, Contacto = "0", Importe = 134.16m, Fecha = AHORA.AddDays(2), Comentario = "Por la tarde"
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(134.16m, repositorio.EnvioGuardado.Reembolso);
            Assert.AreEqual(0, repositorio.EfectosGuardados.Count);
            Assert.AreEqual(AHORA.Date.AddDays(2), repositorio.EnvioGuardado.Fecha, "Sale en el reparto desde el día que se va a cobrar");
            Assert.AreEqual("Cobrar 134,16 €. Por la tarde", repositorio.EnvioGuardado.Observaciones);
        }

        [TestMethod]
        public async Task Crear_FechaPasada_Hoy()
        {
            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0", Importe = 10m, Fecha = AHORA.AddDays(-3) }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(AHORA.Date, repositorio.EnvioGuardado.Fecha);
        }

        [TestMethod]
        public async Task Crear_SinEfectosNiImporte_400()
        {
            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0" }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            Assert.IsNull(repositorio.CabeceraGuardada);
        }

        [TestMethod]
        public async Task Crear_EfectoDeOtroCliente_O_YaCobrado_400()
        {
            repositorio.Efectos.Add(Efecto(501, 100m, cliente: "99999"));
            repositorio.Efectos.Add(Efecto(502, 0m));

            ResultadoNotaCobro deOtro = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Efectos = new List<int> { 501 } }, MIGUEL);
            ResultadoNotaCobro cobrado = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Efectos = new List<int> { 502 } }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, deOtro.Tipo);
            StringAssert.Contains(deOtro.Mensaje, "99999");
            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, cobrado.Tipo);
            StringAssert.Contains(cobrado.Mensaje, "no está pendiente");
            Assert.IsNull(repositorio.CabeceraGuardada);
        }

        [TestMethod]
        public async Task Crear_ClienteQueNoEsDeNuestraRuta_400()
        {
            repositorio.Fichas[CLIENTE + "/0"].Ruta = "FW";

            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0", Importe = 50m }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "FW");
            Assert.IsNull(repositorio.CabeceraGuardada);
        }

        [TestMethod]
        public async Task Crear_YaTieneOtraNotaDeCobroEnElReparto_409()
        {
            repositorio.EnReparto.Add(929600);

            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0", Importe = 50m }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Conflicto, r.Tipo);
            StringAssert.Contains(r.Mensaje, "NC929600");
        }

        [TestMethod]
        public async Task Crear_SinScript_400()
        {
            repositorio.Script = false;

            ResultadoNotaCobro r = await servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0", Importe = 50m }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            Assert.AreEqual(ServicioNotasCobro.MENSAJE_FALTA_SCRIPT, r.Mensaje);
        }

        [TestMethod]
        public async Task Crear_SiFallaAlGuardar_DevuelveElNumeroDePedido()
        {
            repositorio.FalloGuardar = new InvalidOperationException("caída");

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                servicio.Crear(new CrearNotaCobroDTO { Cliente = CLIENTE, Contacto = "0", Importe = 50m }, MIGUEL));

            CollectionAssert.AreEqual(new[] { NotasCobroFalsas.PEDIDO }, repositorio.Devueltos);
        }

        [TestMethod]
        public void NotaDeCobro_NoPideLaGuardiaDelPacking_NiSeCreaComoNotaDeEntrega()
        {
            Assert.IsNull(GuardiaNotaSinPacking.MotivoSinPacking(new EstadoNotaPacking { NotaEntrega = true, Tipo = TiposNotaEntrega.COBRO }, 929700),
                "Sin picking ni packing a propósito");
            Assert.AreEqual(TiposNotaEntrega.COBRO, TiposNotaEntrega.Normalizar(" cobro "));
            Assert.AreEqual("NC929700", TiposNotaEntrega.CodigoDocumento(TiposNotaEntrega.COBRO, 929700));
            Assert.AreEqual("NOTA DE COBRO", TiposNotaEntrega.Titulo(TiposNotaEntrega.COBRO));
        }

        [TestMethod]
        public async Task PutCabecera_TipoCobro_400_SeCreaAparte()
        {
            var cabeceras = A.Fake<IRepositorioNotasEntregaCabecera>();
            A.CallTo(() => cabeceras.LeerPedido("1", 929700)).Returns(new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = true });
            A.CallTo(() => cabeceras.ScriptAplicado()).Returns(true);
            var cabecera = new ServicioNotasEntregaCabecera(cabeceras, _ => { });

            ResultadoNotaEntregaCabecera r = await cabecera.Actualizar("1", 929700, new ActualizarNotaEntregaCabeceraDTO { Tipo = "Cobro" }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            Assert.AreEqual(ServicioNotasEntregaCabecera.MENSAJE_COBRO_APARTE, r.Mensaje);
        }

        [TestMethod]
        public async Task PutCabecera_DeUnaNotaDeCobro_NoSeCambiaDeTipo()
        {
            var cabeceras = A.Fake<IRepositorioNotasEntregaCabecera>();
            A.CallTo(() => cabeceras.LeerPedido("1", 929700)).Returns(new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = true });
            A.CallTo(() => cabeceras.ScriptAplicado()).Returns(true);
            A.CallTo(() => cabeceras.Leer("1", 929700)).Returns(new FilaNotaEntregaCabecera { Empresa = "1", Pedido = 929700, Tipo = "Cobro", Bultos = 0 });
            var cabecera = new ServicioNotasEntregaCabecera(cabeceras, _ => { });

            ResultadoNotaEntregaCabecera r = await cabecera.Actualizar("1", 929700, new ActualizarNotaEntregaCabeceraDTO { Tipo = "Entrega" }, MIGUEL);

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            A.CallTo(() => cabeceras.Guardar(A<FilaNotaEntregaCabecera>._, A<Modificacion>._)).MustNotHaveHappened();
        }

        // ================================================================ la parada en el reparto

        private const int ENVIO_NC = 251400;
        private const int PEDIDO_NC = 929700;

        private ServicioRepartoPropioTests.RepositorioFalso reparto;
        private ParadasRepartoTests.NotasFalsas notas;
        private CargaFurgonetaTests.CargaFalsa cargas;
        private IRepositorioCobrosReparto cobros;
        private ServicioRepartoPropio repartoServicio;
        private List<PreContabilidad> contabilizadas;
        private Guid? claveCobro;
        private CobroTarjetaRepartoTests.TarjetasFalsas tarjetas;
        private CobroTarjetaRepartoTests.AutorizacionesFalsas autorizaciones;

        private void PrepararReparto(params EfectoCobroReparto[] efectos)
        {
            reparto = new ServicioRepartoPropioTests.RepositorioFalso();
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(ENVIO_NC, reembolso: 134.16m, bultos: 0);
            envio.Pedido = PEDIDO_NC;
            envio.Fecha = AHORA.Date;
            reparto.Con(envio);
            notas = new ParadasRepartoTests.NotasFalsas();
            notas.Filas.Add(new NotaParadaReparto
            {
                Pedido = PEDIDO_NC,
                Tipo = TiposNotaEntrega.COBRO,
                ImporteCobro = 134.16m,
                EfectosCobro = efectos.ToList(),
                Texto = ServicioNotasCobro.Texto(134.16m, efectos)
            });
            cargas = new CargaFurgonetaTests.CargaFalsa();
            cobros = A.Fake<IRepositorioCobrosReparto>();
            A.CallTo(() => cobros.LeerPorClave(A<Guid>._)).Returns(Task.FromResult<ResultadoCobroDeudaDTO>(null));
            A.CallTo(() => cobros.LeerEfectos(A<IReadOnlyCollection<int>>._))
                .ReturnsLazily((IReadOnlyCollection<int> ids) => Task.FromResult(efectos.Where(e => ids.Contains(e.Id)).ToList()));
            A.CallTo(() => cobros.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._))
                .ReturnsLazily((Guid clave, string usuario, List<PreContabilidad> lineas, ResultadoCobroDeudaDTO r) =>
                {
                    contabilizadas = lineas;
                    claveCobro = clave;
                    return Task.FromResult(4321);
                });
            var parametrosCobro = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametrosCobro.LeerParametroSinDefecto("1", MIGUEL, "CajaDefecto")).Returns(CAJA_MIGUEL);
            A.CallTo(() => parametrosCobro.LeerParametro("1", MIGUEL, "DiarioCaja")).Returns("_Caja");
            A.CallTo(() => parametrosCobro.LeerParametro("1", MIGUEL, "DelegaciónDefecto")).Returns("ALG");
            A.CallTo(() => parametrosCobro.LeerParametro("1", MIGUEL, "FormaVentaDefecto")).Returns("VAR");
            var parametros = new ServicioRepartoPropioTests.ParametrosFalsos();
            parametros.Caja(MIGUEL, CAJA_MIGUEL);
            tarjetas = new CobroTarjetaRepartoTests.TarjetasFalsas();
            autorizaciones = new CobroTarjetaRepartoTests.AutorizacionesFalsas();
            repartoServicio = new ServicioRepartoPropio(reparto, () => AHORA, null, parametros, () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(),
                cargas, new DependenciasParadasReparto { NotasParada = notas })
            {
                CobroEntrega = new ComprobadorCobroEntrega(tarjetas, autorizaciones, A.Fake<IAvisadorAutorizacionesReembolso>(), () => AHORA),
                CobrosDeuda = new ServicioCobrosReparto(cobros, parametrosCobro, () => AHORA)
            };
        }

        [TestMethod]
        public async Task Envios_LaNotaDeCobroSaleComoNotaCobro_ConSusEfectos_ElImporte_YSinBultos()
        {
            PrepararReparto(Efecto(501, 100m), Efecto(502, 34.16m, "NV26/001300"));

            ResultadoReparto<List<EnvioRepartoDTO>> r = await repartoServicio.Envios(AHORA);

            EnvioRepartoDTO parada = r.Contenido.Single(p => p.EnvioId == ENVIO_NC);
            Assert.AreEqual(TiposParadaReparto.NOTA_COBRO, parada.TipoParada);
            Assert.IsTrue(parada.EsNotaCobro);
            Assert.AreEqual("NC929700", parada.CodigoDocumento);
            Assert.AreEqual(0, parada.Bultos);
            Assert.AreEqual(134.16m, parada.Reembolso);
            CollectionAssert.AreEqual(new[] { 501, 502 }, parada.EfectosCobro.Select(e => e.Id).ToArray());
            Assert.AreEqual("1", parada.EfectosCobro[0].Empresa);
            Assert.AreEqual("NV26/001234", parada.EfectosCobro[0].Documento);
            Assert.IsTrue(parada.AdmiteTarjeta);
            StringAssert.StartsWith(parada.TextoNota, "Cobro de 134,16 €\n");
        }

        [TestMethod]
        public async Task Envios_ConUnEfectoDeLaEmpresa3_SinTarjeta()
        {
            PrepararReparto(Efecto(501, 100m), Efecto(502, 34.16m, "GB26/000100", empresa: "3  "));

            ResultadoReparto<List<EnvioRepartoDTO>> r = await repartoServicio.Envios(AHORA);

            EnvioRepartoDTO parada = r.Contenido.Single(p => p.EnvioId == ENVIO_NC);
            Assert.AreEqual("3", parada.EmpresaCobro);
            Assert.IsFalse(parada.AdmiteTarjeta, "La 3 se cobra siempre en efectivo");
        }

        [TestMethod]
        public async Task SinCargar_NoSaleLaNotaDeCobro_NoTieneBultos()
        {
            PrepararReparto(Efecto(501, 134.16m));

            ResultadoReparto<List<EnvioSinCargarDTO>> r = await repartoServicio.SinCargar(AHORA);

            Assert.IsFalse(r.Contenido.Any(e => e.EnvioId == ENVIO_NC));
        }

        [TestMethod]
        public async Task Entregar_EnEfectivo_LiquidaLosEfectosDeLaNota_EnLaCajaDelRepartidor_ConLaClaveDeLaEntrega()
        {
            PrepararReparto(Efecto(501, 100m), Efecto(502, 34.16m, "NV26/001300"));
            var clave = Guid.NewGuid();

            ResultadoReparto<EstadoEnvioRepartoDTO> r = await repartoServicio.Entregar(ENVIO_NC, new EntregaRepartoDTO
            {
                ClaveIdempotencia = clave, CobroReembolso = "Efectivo", ImporteCobrado = 134.16m, Receptor = "Lucía"
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(clave, claveCobro, "Idempotente con la misma clave que la entrega");
            List<PreContabilidad> clientes = contabilizadas.Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE).ToList();
            CollectionAssert.AreEqual(new int?[] { 501, 502 }, clientes.Select(l => l.Liquidado).ToList());
            CollectionAssert.AreEqual(new[] { 100m, 34.16m }, clientes.Select(l => l.Haber).ToList());
            PreContabilidad caja = contabilizadas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(CAJA_MIGUEL, caja.Nº_Cuenta);
            Assert.AreEqual(134.16m, caja.Debe);

            ServicioRepartoPropioTests.Registro registro = reparto.Registros.Single();
            Assert.IsNull(registro.ImporteAContabilizar, "No se contabiliza otra vez como reembolso del envío");
            Assert.AreEqual(4321, registro.Prueba.Asiento);
            Assert.AreEqual(CAJA_MIGUEL, registro.Prueba.CuentaCaja);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, reparto.Envios[ENVIO_NC].Estado);
            StringAssert.StartsWith(registro.Historia[0].Observaciones, "Reparto propio: nota de cobro (efectivo 134,16 €)");
        }

        [TestMethod]
        public async Task Entregar_MasDeLoPendiente_LoQueSobraACuenta()
        {
            PrepararReparto(Efecto(501, 100m));

            ResultadoReparto<EstadoEnvioRepartoDTO> r = await repartoServicio.Entregar(ENVIO_NC, new EntregaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "Efectivo", ImporteCobrado = 134.16m
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            PreContabilidad aCuenta = contabilizadas.Single(l => l.Nº_Documento == "A CUENTA" && l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE);
            Assert.AreEqual(34.16m, aCuenta.Haber);
        }

        [TestMethod]
        public async Task Entregar_SinCobrar_PideAutorizacion_YNoContabilizaNada()
        {
            PrepararReparto(Efecto(501, 134.16m));

            ResultadoReparto<EstadoEnvioRepartoDTO> r = await repartoServicio.Entregar(ENVIO_NC, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "autorización");
            Assert.AreEqual(0, reparto.Registros.Count);
            Assert.IsNull(contabilizadas);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, reparto.Envios[ENVIO_NC].Estado);
        }

        [TestMethod]
        public async Task Entregar_SinCobrar_ConAutorizacionPorTelefono_SeCierraSinContabilizar()
        {
            PrepararReparto(Efecto(501, 134.16m));

            ResultadoReparto<EstadoEnvioRepartoDTO> r = await repartoServicio.Entregar(ENVIO_NC, new EntregaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "NoCobrado", AutorizacionTelefonica = "Autoriza Carlos por teléfono"
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsNull(contabilizadas);
            Assert.IsNull(reparto.Registros.Single().ImporteAContabilizar);
            Assert.AreEqual(EstadosAutorizacionReembolso.TELEFONICA, autorizaciones.Filas.Single().Estado);
        }

        [TestMethod]
        public async Task Entregar_SiFallaElCobro_NoSeEntrega()
        {
            PrepararReparto(Efecto(501, 134.16m));
            A.CallTo(() => cobros.Registrar(A<Guid>._, A<string>._, A<List<PreContabilidad>>._, A<ResultadoCobroDeudaDTO>._))
                .Throws(new NestoAPI.Infraestructure.Exceptions.NestoBusinessException("Fecha no permitida"));

            ResultadoReparto<EstadoEnvioRepartoDTO> r = await repartoServicio.Entregar(ENVIO_NC, new EntregaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "Efectivo", ImporteCobrado = 134.16m
            }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "Fecha no permitida");
            Assert.AreEqual(0, reparto.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, reparto.Envios[ENVIO_NC].Estado);
        }

        // ================================================================ tarjeta

        [TestMethod]
        public async Task EnlaceTarjeta_DeUnaNotaDeCobro_LlevaSusEfectos()
        {
            PrepararReparto(Efecto(501, 100m), Efecto(502, 34.16m, "NV26/001300"));
            SolicitudPagoTPV solicitud = null;
            var pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).ReturnsLazily((SolicitudPagoTPV s, string u) =>
            {
                solicitud = s;
                tarjetas.Pagos.Add(new PagoCadenaReparto { Id = 9001, Estado = Constantes.EstadosPagoTPV.PENDIENTE, Importe = s.Importe, FechaCreacion = AHORA, TokenAcceso = Guid.NewGuid() });
                return new RespuestaIniciarPago { IdPago = 9001, UrlPaginaPago = "https://api.nuevavision.es/pago/x" };
            });
            var tarjeta = new ServicioCobroTarjetaReparto(reparto, tarjetas, () => pagos, () => AHORA, notas);

            ResultadoReparto<EnlaceTarjetaRepartoDTO> r = await tarjeta.EnlaceEnvio(ENVIO_NC, new PeticionEnlaceTarjetaDTO(), MIGUEL, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(134.16m, solicitud.Importe);
            CollectionAssert.AreEqual(new[] { 501, 502 }, solicitud.Efectos.Select(e => e.ExtractoClienteId).ToArray());
            Assert.AreEqual(134.16m, solicitud.Efectos.Sum(e => e.Importe), "El asiento del cobro con tarjeta cuadra");
            StringAssert.Contains(r.Contenido.Texto, "los documentos NV26/001234, NV26/001300");
        }

        [TestMethod]
        public async Task EnlaceTarjeta_DeUnaNotaDeCobroConEfectoDeLa3_400()
        {
            PrepararReparto(Efecto(501, 134.16m, "GB26/000100", empresa: "3  "));
            var pagos = A.Fake<IServicioPagos>();
            var tarjeta = new ServicioCobroTarjetaReparto(reparto, tarjetas, () => pagos, () => AHORA, notas);

            ResultadoReparto<EnlaceTarjetaRepartoDTO> r = await tarjeta.EnlaceEnvio(ENVIO_NC, new PeticionEnlaceTarjetaDTO(), MIGUEL, "PDA1");

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void LiquidacionNotaCobro_ElUltimoEnParte_YSiPasaDeLoPendienteACuenta()
        {
            var efectos = new[] { Efecto(1, 100m), Efecto(2, 50m) };

            LiquidacionReembolsoTarjeta parte = ServicioCobroTarjetaReparto.LiquidacionNotaCobro(efectos, 120m);
            LiquidacionReembolsoTarjeta pasa = ServicioCobroTarjetaReparto.LiquidacionNotaCobro(efectos, 151m);
            LiquidacionReembolsoTarjeta sin = ServicioCobroTarjetaReparto.LiquidacionNotaCobro(new EfectoCobroReparto[0], 20m);

            CollectionAssert.AreEqual(new[] { 100m, 20m }, parte.AlEnlace().Select(e => e.Importe).ToArray());
            Assert.IsNull(pasa.AlEnlace(), "Sumarían menos que el pago y el asiento no cuadraría: a cuenta");
            StringAssert.Contains(pasa.MotivoACuenta, "pasa de lo pendiente");
            Assert.IsNull(sin.AlEnlace());
        }

        // ================================================================ la firma

        [TestMethod]
        public void Conformidad_DeUnaNotaDeCobro_HePagado()
        {
            Assert.AreEqual("Confirmo que he pagado 134,16 € en efectivo al repartidor de Nueva Visión (nota NC929700).",
                TextoConformidadEntrega.Cobro(929700, FormaPagoConformidad.Efectivo, 134.16m));
            Assert.AreEqual("Confirmo que he pagado 134,16 € con tarjeta a Nueva Visión (nota NC929700).",
                TextoConformidadEntrega.Cobro(929700, FormaPagoConformidad.Tarjeta, 134.16m));
            Assert.AreEqual("Confirmo que el repartidor de Nueva Visión ha pasado a cobrar la nota NC929700 (134,16 €).",
                TextoConformidadEntrega.Cobro(929700, FormaPagoConformidad.Ninguno, 134.16m));
            Assert.AreEqual("Confirmo que he pagado 10,00 € en efectivo al repartidor de Nueva Visión.",
                TextoConformidadEntrega.Cobro(null, FormaPagoConformidad.Efectivo, 10m));

            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(ENVIO_NC, reembolso: 134.16m, bultos: 0);
            envio.Pedido = PEDIDO_NC;
            Assert.AreEqual("Confirmo que he pagado 100,00 € en efectivo al repartidor de Nueva Visión (nota NC929700).",
                ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO(), envio, false, null, "Efectivo", 100m, esCobro: true),
                "Sin bultos: lo de una PDA vieja también dice «he pagado»");
        }

        // ================================================================ dobles

        internal class NotasCobroFalsas : IRepositorioNotasCobro
        {
            public const int PEDIDO = 929700;
            public const int ENVIO = 251400;

            public bool Script = true;
            public readonly Dictionary<string, FichaNotaCobro> Fichas = new Dictionary<string, FichaNotaCobro>();
            public readonly List<EfectoCobroReparto> Efectos = new List<EfectoCobroReparto>();
            public readonly List<int> EnReparto = new List<int>();
            public readonly List<int> Devueltos = new List<int>();
            public Exception FalloGuardar;
            public CabPedidoVta CabeceraGuardada;
            public FilaNotaEntregaCabecera FilaGuardada;
            public decimal ImporteGuardado;
            public List<int> EfectosGuardados;
            public EnviosAgencia EnvioGuardado;
            public string HistoriaGuardada;

            public bool ScriptAplicado() => Script;
            public Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto)
                => Task.FromResult(Fichas.TryGetValue(cliente + "/" + contacto, out FichaNotaCobro f) ? f : null);
            public Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids) => Task.FromResult(Efectos.Where(e => ids.Contains(e.Id)).ToList());
            public Task<List<int>> LeerNotasEnReparto(string empresa, string cliente, string contacto) => Task.FromResult(EnReparto.ToList());
            public Task<string> LeerEmpresaAgencia() => Task.FromResult("1");
            public int ReservarNumeroPedido() => PEDIDO;
            public void DevolverNumeroPedido(int numero) => Devueltos.Add(numero);
            public Task<int> Guardar(CabPedidoVta cabecera, FilaNotaEntregaCabecera fila, decimal importe, IReadOnlyList<int> efectos,
                EnviosAgencia envio, string historia, Modificacion modificacion)
            {
                if (FalloGuardar != null)
                {
                    throw FalloGuardar;
                }
                CabeceraGuardada = cabecera;
                FilaGuardada = fila;
                ImporteGuardado = importe;
                EfectosGuardados = efectos.ToList();
                EnvioGuardado = envio;
                HistoriaGuardada = historia;
                envio.Numero = ENVIO;
                return Task.FromResult(ENVIO);
            }
            public Task<Dictionary<int, DatosNotaCobro>> LeerDatos(string empresa, IReadOnlyCollection<int> pedidos)
                => Task.FromResult(new Dictionary<int, DatosNotaCobro>());
        }
    }
}
