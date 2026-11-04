using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#801/#807: los cobros del reparto propio van a la empresa de la factura pendiente del pedido en el EXTRACTO del cliente,
    /// no a EnviosAgencia.Empresa (siempre «1») ni a la del pedido (un pedido de la 1 puede facturarse en la 3 por el traspaso a la
    /// empresa espejo). Caso real del 03/11/26: 5825 / pedido 929679, 137,31 € en efectivo, contabilizado en la 1 a cuenta en vez de
    /// liquidar la GB2601735 (de la 3).
    /// </summary>
    [TestClass]
    public class EmpresaDelCobroRepartoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 3);
        private const string USUARIO = @"NUEVAVISION\Miguel";
        private const string CAJA_MIGUEL = "57000032";

        // ================================================================ la regla (pura)

        private static FacturaPedidoReparto Factura(string empresa, string factura = "GB2601735", int pedido = 929679)
            => new FacturaPedidoReparto { Pedido = pedido, Empresa = empresa, Factura = factura };

        private static ExtractoCliente Pendiente(int orden, string empresa, string documento, decimal pendiente, DateTime? fecha = null,
            string cliente = "5825      ", string estado = Constantes.ExtractosCliente.Estados.NORMAL) => new ExtractoCliente
            {
                Nº_Orden = orden, Empresa = empresa, Número = cliente, Contacto = "0  ", Nº_Documento = documento, Efecto = "1",
                Importe = pendiente, ImportePdte = pendiente, Fecha = fecha ?? HOY, Estado = estado, Delegación = "ALG", FormaVenta = "TEL", Ruta = "AT"
            };

        [TestMethod]
        public void DeEnvio_PedidoDeLa1FacturadoEnLa3_CobroEnLa3LiquidandoLaGB()
        {
            // NestoAPI#807: el pedido es de la 1 (y el envío dice 1), pero la factura pendiente GB está en la 3 (traspaso a la espejo)
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(),
                new[] { Factura("1  ") },
                new[] { Pendiente(7001, "3  ", "GB2601735 ", 137.31M), Pendiente(6999, "1  ", "NV26000001", 137.31M) },
                "1  ");

            Assert.AreEqual("3", cobro.Empresa);
            Assert.AreEqual(7001, cobro.Movimiento?.Nº_Orden, "Liquida la GB de la 3, no la de la 1 por importe");
        }

        [TestMethod]
        public void DeEnvio_PedidoDeLa3_La3()
        {
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(), new[] { Factura("3  ") },
                new[] { Pendiente(7001, "3  ", "GB2601735", 137.31M) }, "3  ");

            Assert.AreEqual("3", cobro.Empresa);
            Assert.AreEqual(7001, cobro.Movimiento?.Nº_Orden);
        }

        [TestMethod]
        public void DeEnvio_SinFacturaTodavia_LaEmpresaDelPedido()
        {
            // Lo pendiente del cliente no es del pedido ni por el importe del reembolso
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(), new FacturaPedidoReparto[0],
                new[] { Pendiente(6999, "1  ", "NV26000001", 50M) }, "3  ");

            Assert.AreEqual("3", cobro.Empresa);
            Assert.IsNull(cobro.Movimiento);
        }

        [TestMethod]
        public void DeEnvio_SinFacturaNiPedido_LaDelEnvio_YSinNadaLa1()
        {
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeEnvio(Envio5825(), null, null, null).Empresa);
            EnviosAgencia sinEmpresa = Envio5825();
            sinEmpresa.Empresa = null;
            Assert.AreEqual(Constantes.Empresas.EMPRESA_POR_DEFECTO, EmpresaDelCobroReparto.DeEnvio(sinEmpresa, null, null, "  ").Empresa);
        }

        [TestMethod]
        public void DeEnvio_FacturaSinNadaPendiente_LaEmpresaDeLaFactura_SinMovimiento()
        {
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(), new[] { Factura("3  ") },
                new[] { Pendiente(6999, "1  ", "NV26000001", 137.31M) }, "1  ");

            Assert.AreEqual("3", cobro.Empresa, "La factura del pedido manda sobre lo pendiente por importe de otra empresa");
            Assert.IsNull(cobro.Movimiento, "Se busca como siempre en la 3");
        }

        [TestMethod]
        public void DeEnvio_VariosPendientesDeLaFactura_ElDeMayorPendiente_YSiEmpatanElMasReciente()
        {
            var facturas = new[] { Factura("1  ", "GB2601735"), Factura("1  ", "GB2601736") };
            CobroEnvioReparto mayor = EmpresaDelCobroReparto.DeEnvio(Envio5825(), facturas, new[]
            {
                Pendiente(7001, "3  ", "GB2601735", 40M),
                Pendiente(7002, "3  ", "GB2601736", 97.31M)
            }, "1");
            Assert.AreEqual(7002, mayor.Movimiento.Nº_Orden);

            CobroEnvioReparto reciente = EmpresaDelCobroReparto.DeEnvio(Envio5825(), facturas, new[]
            {
                Pendiente(7001, "1  ", "GB2601735", 68.65M, HOY.AddDays(-1)),
                Pendiente(7002, "3  ", "GB2601736", 68.65M, HOY)
            }, "1");
            Assert.AreEqual("3", reciente.Empresa);
            Assert.AreEqual(7002, reciente.Movimiento.Nº_Orden);
        }

        [TestMethod]
        public void DeEnvio_SinFactura_ElPendienteDelClientePorElImporteDelReembolso_EnCualquierEmpresa()
        {
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(), new FacturaPedidoReparto[0], new[]
            {
                Pendiente(6999, "1  ", "NV26000001", 50M),
                Pendiente(7001, "3  ", "GB2601735", 137.31M),
                Pendiente(7005, "1  ", "NV26000005", 137.31M, HOY.AddDays(1), estado: "DVD")
            }, "1  ");

            Assert.AreEqual("3", cobro.Empresa);
            Assert.AreEqual(7001, cobro.Movimiento?.Nº_Orden, "El de otro estado no cuenta (como el efectivo de siempre)");
        }

        [TestMethod]
        public void DeEnvio_PendientesDeOtroClienteYOtroPedido_NoCuentan()
        {
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(),
                new[] { Factura("3  ", "GB2609999", pedido: 111111) },
                new[] { Pendiente(7001, "3  ", "GB2609999", 137.31M, cliente: "4444      ") }, "1  ");

            Assert.AreEqual("1", cobro.Empresa);
            Assert.IsNull(cobro.Movimiento);
        }

        [TestMethod]
        public void DeEnvio_FacturaPendienteConOtroSigno_DaLaEmpresaPeroNoSeLiquida()
        {
            CobroEnvioReparto cobro = EmpresaDelCobroReparto.DeEnvio(Envio5825(), new[] { Factura("1  ") },
                new[] { Pendiente(7001, "3  ", "GB2601735", -20M) }, "1  ");

            Assert.AreEqual("3", cobro.Empresa);
            Assert.IsNull(cobro.Movimiento, "Un cobro no liquida un abono");
        }

        [TestMethod]
        public void DeACuenta_EfectosMarcadosDeUnaSolaEmpresa_EsaEmpresa()
        {
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeACuenta(new[] { "3  ", "3" }, "1"));
        }

        [TestMethod]
        public void DeACuenta_EfectosMarcadosDeDosEmpresas_La1()
        {
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(new[] { "1  ", "3  " }, "3"));
        }

        [TestMethod]
        public void DeACuenta_SinMarcados_LaDeLaUltimaPendiente_YSinNadaLa1()
        {
            Assert.AreEqual("3", EmpresaDelCobroReparto.DeACuenta(new string[0], "3  "));
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(null, null));
            Assert.AreEqual("1", EmpresaDelCobroReparto.DeACuenta(new string[0], ""));
        }

        // ================================================================ la lectura (tres consultas para toda la lista)

        private static NVEntities DbDelExtracto(List<LinPedidoVta> lineas, List<CabPedidoVta> pedidos, List<ExtractoCliente> extracto)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<LinPedidoVta> setLineas = FakeDbSet(lineas);
            DbSet<CabPedidoVta> setPedidos = FakeDbSet(pedidos);
            DbSet<ExtractoCliente> setExtracto = FakeDbSet(extracto);
            A.CallTo(() => db.LinPedidoVtas).Returns(setLineas);
            A.CallTo(() => db.CabPedidoVtas).Returns(setPedidos);
            A.CallTo(() => db.ExtractosCliente).Returns(setExtracto);
            return db;
        }

        [TestMethod]
        public async Task DeEnviosAsync_VariosEnvios_UnaLecturaDeCadaTabla_YCadaUnoConSuEmpresa()
        {
            // 250446: pedido de la 1 facturado en la 3 (GB pendiente en la 3); 250447: pedido de la 3 sin facturar; 250448: pedido de
            // la 1 facturado en la 1 y pendiente
            NVEntities db = DbDelExtracto(
                new List<LinPedidoVta>
                {
                    new LinPedidoVta { Empresa = "1  ", Número = 929679, Nº_Factura = "GB2601735" },
                    new LinPedidoVta { Empresa = "1  ", Número = 929679, Nº_Factura = "GB2601735" },
                    new LinPedidoVta { Empresa = "3  ", Número = 929680, Nº_Factura = null },
                    new LinPedidoVta { Empresa = "1  ", Número = 929681, Nº_Factura = "NV26012345" }
                },
                new List<CabPedidoVta>
                {
                    new CabPedidoVta { Empresa = "1  ", Número = 929679 },
                    new CabPedidoVta { Empresa = "3  ", Número = 929680 },
                    new CabPedidoVta { Empresa = "1  ", Número = 929681 }
                },
                new List<ExtractoCliente>
                {
                    Pendiente(7001, "3  ", "GB2601735", 137.31M),
                    Pendiente(7002, "1  ", "NV26012345", 20M, cliente: "15191     ")
                });
            EnviosAgencia gb = Envio5825();
            EnviosAgencia sinFacturar = Envio5825();
            sinFacturar.Numero = 250447;
            sinFacturar.Pedido = 929680;
            sinFacturar.Reembolso = 33M;
            EnviosAgencia dela1 = Envio5825();
            dela1.Numero = 250448;
            dela1.Pedido = 929681;
            dela1.Cliente = "15191     ";
            dela1.Reembolso = 20M;

            Dictionary<int, CobroEnvioReparto> cobros = await EmpresaDelCobroReparto.DeEnviosAsync(db, new[] { gb, sinFacturar, dela1 });

            Assert.AreEqual("3", cobros[250446].Empresa);
            Assert.AreEqual(7001, cobros[250446].Movimiento?.Nº_Orden);
            Assert.AreEqual("3", cobros[250447].Empresa, "Sin facturar: la del pedido");
            Assert.IsNull(cobros[250447].Movimiento);
            Assert.AreEqual("1", cobros[250448].Empresa);
            Assert.AreEqual(7002, cobros[250448].Movimiento?.Nº_Orden);
            A.CallTo(() => db.LinPedidoVtas).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.CabPedidoVtas).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.ExtractosCliente).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeEnvioAsync_YContabilizar_PedidoDeLa1FacturadoEnLa3_ApunteEnLa3LiquidandoLaGB()
        {
            // NestoAPI#807, el efectivo al entregar de punta a punta (lo que hace RepositorioRepartoPropio.Registrar): la regla da la 3
            // y la GB, y el reembolso se contabiliza en la 3 liquidándola, aunque el pedido y el envío sean de la 1.
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            DbSet<LinPedidoVta> setLineas = FakeDbSet(new List<LinPedidoVta> { new LinPedidoVta { Empresa = "1  ", Número = 929679, Nº_Factura = "GB2601735" } });
            DbSet<CabPedidoVta> setPedidos = FakeDbSet(new List<CabPedidoVta> { new CabPedidoVta { Empresa = "1  ", Número = 929679 } });
            A.CallTo(() => db.LinPedidoVtas).Returns(setLineas);
            A.CallTo(() => db.CabPedidoVtas).Returns(setPedidos);
            EnviosAgencia envio = Envio5825();

            CobroEnvioReparto destino = await EmpresaDelCobroReparto.DeEnvioAsync(db, envio);
            int asiento = await new TramitacionEnviosService(db, contabilidad, () => HOY)
                .ContabilizarReembolsoAsync(envio, USUARIO, 137.31M, CAJA_MIGUEL, destino.Empresa, destino.Movimiento);

            Assert.AreEqual(164925, asiento, "El diario de la empresa 3");
            PreContabilidad linea = lineas.Single();
            Assert.AreEqual("3", linea.Empresa);
            Assert.AreEqual(7001, linea.Liquidado, "Liquida la GB de la 3, no queda a cuenta");
            Assert.AreEqual("GB2601735", linea.Nº_Documento);
            Assert.AreEqual("EF3", linea.FormaPago);
        }

        [TestMethod]
        public async Task ContabilizarReembolso_ConMovimientoDado_LoLiquidaSinBuscarEnElExtracto()
        {
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            ExtractoCliente gb = Pendiente(7777, "3  ", "GB2601799", 137.31M);

            _ = await new TramitacionEnviosService(db, contabilidad, () => HOY)
                .ContabilizarReembolsoAsync(Envio5825(), USUARIO, 137.31M, CAJA_MIGUEL, "3", gb);

            Assert.AreEqual(7777, lineas.Single().Liquidado);
            Assert.AreEqual("GB2601799", lineas.Single().Nº_Documento);
            A.CallTo(() => db.ExtractosCliente).MustNotHaveHappened();
        }

        // ================================================================ las paradas y el enlace al entregar (ServicioRepartoPropio)

        private static ServicioRepartoPropio ServicioReparto(ServicioRepartoPropioTests.RepositorioFalso repositorio, IServicioPagos pagos)
        {
            var parametros = new ServicioRepartoPropioTests.ParametrosFalsos();
            parametros.Caja("Repartidor", "57000001");
            return new ServicioRepartoPropio(repositorio, () => HOY.AddHours(12), null, parametros, () => pagos);
        }

        [TestMethod]
        public async Task Envios_ParadaConFacturaPendienteEnLa3_NoAdmiteTarjeta_ConUnaSolaLecturaParaTodaLaLista()
        {
            var repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            AlDia(repositorio.Con(ServicioRepartoPropioTests.Envio(numero: 1, reembolso: 137.31M)));
            AlDia(repositorio.Con(ServicioRepartoPropioTests.Envio(numero: 2, reembolso: 40M)));
            AlDia(repositorio.Con(ServicioRepartoPropioTests.Envio(numero: 3)));
            repositorio.Cobros[1] = new CobroEnvioReparto { Empresa = "3" };
            repositorio.Cobros[2] = new CobroEnvioReparto { Empresa = "1" };

            List<EnvioRepartoDTO> lista = (await ServicioReparto(repositorio, A.Fake<IServicioPagos>()).Envios(HOY)).Contenido;

            Assert.IsFalse(lista.Single(p => p.EnvioId == 1).AdmiteTarjeta, "La factura pendiente es de la 3: siempre en efectivo");
            Assert.AreEqual("1", lista.Single(p => p.EnvioId == 1).Empresa, "La empresa del envío no cambia");
            Assert.AreEqual("3", lista.Single(p => p.EnvioId == 1).EmpresaCobro);
            Assert.IsTrue(lista.Single(p => p.EnvioId == 2).AdmiteTarjeta);
            Assert.IsTrue(lista.Single(p => p.EnvioId == 3).AdmiteTarjeta);
            Assert.AreEqual(1, repositorio.LecturasCobros, "Una sola lectura para toda la lista (sin N+1)");
            Assert.AreEqual(2, repositorio.EnviosUltimaLecturaCobros, "Solo las paradas con reembolso");
        }

        [TestMethod]
        public async Task Envios_FallaLaLecturaDeLaEmpresaDelCobro_LaListaSaleIgual()
        {
            var repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            AlDia(repositorio.Con(ServicioRepartoPropioTests.Envio(numero: 1, reembolso: 137.31M)));
            repositorio.FalloLeerCobros = new InvalidOperationException("Sin conexión");

            var resultado = await ServicioReparto(repositorio, A.Fake<IServicioPagos>()).Envios(HOY);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(1, resultado.Contenido.Count);
            Assert.IsNull(resultado.Contenido[0].EmpresaCobro);
        }

        [TestMethod]
        public async Task Entregar_EnlaceConFacturaPendienteEnLa3_400_SinEnlaceNiEntrega()
        {
            var repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            EnviosAgencia envio = repositorio.Con(ServicioRepartoPropioTests.Envio(reembolso: 137.31M));
            repositorio.Cobros[251300] = new CobroEnvioReparto { Empresa = "3" };
            IServicioPagos pagos = A.Fake<IServicioPagos>();
            var peticion = new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "Enlace", CorreoEnlace = "cliente@ejemplo.com" };

            var resultado = await ServicioReparto(repositorio, pagos).Entregar(251300, peticion, @"NUEVAVISION\Repartidor");

            Assert.AreEqual(TipoResultadoReparto.Invalido, resultado.Tipo);
            StringAssert.Contains(resultado.Mensaje, "empresa 3");
            StringAssert.Contains(resultado.Mensaje, "efectivo");
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, repositorio.Registros.Count);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
        }

        [TestMethod]
        public async Task Entregar_Enlace_LaSolicitudVaConLaEmpresaDelCobro()
        {
            var repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            EnviosAgencia envio = repositorio.Con(ServicioRepartoPropioTests.Envio(reembolso: 137.31M));
            envio.Empresa = "3  "; // aunque el envío dijera la 3, manda el extracto
            repositorio.Cobros[251300] = new CobroEnvioReparto { Empresa = "1" };
            IServicioPagos pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).Returns(new RespuestaIniciarPago { IdPago = 1, UrlPaginaPago = "https://x/pago/1" });
            var peticion = new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "Enlace", CorreoEnlace = "cliente@ejemplo.com" };

            var resultado = await ServicioReparto(repositorio, pagos).Entregar(251300, peticion, @"NUEVAVISION\Repartidor");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>.That.Matches(s => s.Empresa == "1"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        /// <summary>Los envíos de ServicioRepartoPropioTests son del 29/10/26: aquí, del día de la lista.</summary>
        private static void AlDia(EnviosAgencia envio) => envio.Fecha = HOY;

        // ================================================================ el efectivo al entregar (TramitacionEnviosService)

        private static EnviosAgencia Envio5825() => new EnviosAgencia
        {
            Numero = 250446,
            Empresa = "1  ",
            Cliente = "5825      ",
            Contacto = "0  ",
            Pedido = 929679,
            Vendedor = "NV ",
            Reembolso = 137.31M,
            Empresa1 = new Empresa { Número = "1  ", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
            AgenciasTransporte = new AgenciaTransporte { Numero = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Nombre = "Reparto propio" }
        };

        private static NVEntities DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<ExtractoCliente> extractos = FakeDbSet(new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Nº_Orden = 7001, Empresa = "3", Número = "5825      ", Contacto = "0  ", Nº_Documento = "GB2601735", Efecto = "1",
                    ImportePdte = 137.31M, Importe = 137.31M, Fecha = HOY, Estado = Constantes.ExtractosCliente.Estados.NORMAL,
                    Delegación = "ALG", FormaVenta = "TEL", Ruta = "AT"
                }
            });
            DbSet<Empresa> empresas = FakeDbSet(new List<Empresa>
            {
                new Empresa { Número = "1", FormaPagoEfectivo = "EFC", DelegaciónVarios = "ALG", FormaVentaVarios = "DIR" },
                new Empresa { Número = "3", FormaPagoEfectivo = "EF3", DelegaciónVarios = "GB", FormaVentaVarios = "VAR" }
            });
            A.CallTo(() => db.ExtractosCliente).Returns(extractos);
            A.CallTo(() => db.Empresas).Returns(empresas);
            IContabilidadService fake = A.Fake<IContabilidadService>();
            contabilidad = fake;
            var registradas = new List<PreContabilidad>();
            lineas = registradas;
            A.CallTo(() => fake.CrearLineas(db, A<List<PreContabilidad>>._))
                .ReturnsLazily((NVEntities d, List<PreContabilidad> l) => { registradas.AddRange(l); return Task.FromResult(l.Count); });
            A.CallTo(() => fake.ContabilizarDiario(db, "3", "_Reembolso", USUARIO)).Returns(164925);
            return db;
        }

        [TestMethod]
        public async Task ContabilizarReembolso_PedidoDeLaEmpresa3_ApunteEnLa3LiquidandoSuFactura()
        {
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            int asiento = await servicio.ContabilizarReembolsoAsync(Envio5825(), USUARIO, 137.31M, CAJA_MIGUEL, "3  ");

            Assert.AreEqual(164925, asiento, "Se contabiliza el diario de la empresa 3");
            PreContabilidad linea = lineas.Single();
            Assert.AreEqual("3", linea.Empresa);
            Assert.AreEqual(7001, linea.Liquidado, "Liquida la factura de la 3, no queda a cuenta");
            Assert.AreEqual("GB2601735", linea.Nº_Documento);
            Assert.AreEqual(137.31M, linea.Haber);
            Assert.AreEqual(CAJA_MIGUEL, linea.Contrapartida);
            Assert.AreEqual("EF3", linea.FormaPago, "La forma de pago en efectivo de la empresa 3");
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ContabilizarReembolso_SinEmpresaExplicita_ComoSiempreEnLaDelEnvio()
        {
            // Las agencias de fuera (y la sobrecarga de 4 parámetros) siguen con envio.Empresa: aquí la 1, donde no hay nada pendiente
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out List<PreContabilidad> lineas);
            A.CallTo(() => contabilidad.ContabilizarDiario(db, "1", "_Reembolso", USUARIO)).Returns(1207304);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            int asiento = await servicio.ContabilizarReembolsoAsync(Envio5825(), USUARIO, 137.31M, CAJA_MIGUEL);

            Assert.AreEqual(1207304, asiento);
            PreContabilidad linea = lineas.Single();
            Assert.AreEqual("1", linea.Empresa);
            Assert.IsNull(linea.Liquidado);
            Assert.AreEqual("EFC", linea.FormaPago);
        }

        [TestMethod]
        public async Task BuscarMovimientoALiquidar_EnLaEmpresaDelPedido_EncuentraLaFacturaDeLa3()
        {
            NVEntities db = DbConFacturaDeLa3(out IContabilidadService contabilidad, out _);
            var servicio = new TramitacionEnviosService(db, contabilidad, () => HOY);

            ExtractoCliente movimiento = await servicio.BuscarMovimientoALiquidarAsync(Envio5825(), 137.31M, "3");

            Assert.IsNotNull(movimiento);
            Assert.AreEqual("GB2601735", movimiento.Nº_Documento);
        }

        // ================================================================ utilidades

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            IQueryable<T> data = datos.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)set).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            return set;
        }
    }
}
