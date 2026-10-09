using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (c4): el canje del cheque regalo en los pedidos. Reglas duras fuera del pipeline de validación,
    /// línea normalizada por el servidor, reserva del cheque con UPDATE condicionado y liberación al quitarlo.
    /// </summary>
    [TestClass]
    public class CanjeChequesRegaloTests
    {
        private const string PRODUCTO_CHEQUE = "CHEQUE50_OCT26";
        private const string CAMPANA = "CHEQUE50_OCT_2026";
        private const string CLIENTE = "15191";
        private const int PEDIDO = 930001;
        private static readonly DateTime HOY = new DateTime(2026, 10, 20, 10, 0, 0);

        private IRepositorioCanjeChequesRegalo repositorio;
        private ServicioCanjeChequesRegalo servicio;
        private DateTime ahora;

        [TestInitialize]
        public void Inicializar()
        {
            ahora = HOY;
            repositorio = A.Fake<IRepositorioCanjeChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { Campana() });
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).Returns(Productos());
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque() });
            A.CallTo(() => repositorio.ReservarCheque(A<int>._, A<string>._, A<int>._, A<string>._)).Returns(true);
            servicio = new ServicioCanjeChequesRegalo(repositorio, () => ahora);
        }

        internal static CampanaCanjeChequeRegalo Campana(string codigo = CAMPANA, string producto = PRODUCTO_CHEQUE, bool activa = true) => new CampanaCanjeChequeRegalo
        {
            Codigo = codigo,
            Empresa = "1",
            Producto = producto,
            ImporteBase = 50,
            GeneraDesde = new DateTime(2026, 10, 12),
            GeneraHasta = new DateTime(2026, 10, 29),
            CanjeHasta = new DateTime(2026, 11, 7),
            MinimoFacturaQueGenera = 0,
            MinimoCanje = 250,
            DiasEsperaTrasEntrega = 0,
            Activa = activa,
            PrefijosNombreExcluidosMinimo = "PACK 26",
            GruposExcluidosMinimo = "PEL"
        };

        private static ChequeRegaloCliente Cheque(int id = 7, string campana = CAMPANA, string estado = "Generado", int? pedidoCanje = null,
            bool pedidoCanjeTieneLaLinea = false, DateTime? activacion = null, bool esPedidoDeLaFactura = false) => new ChequeRegaloCliente
        {
            Id = id,
            Campana = campana,
            Empresa = "1",
            Cliente = CLIENTE,
            EmpresaFactura = "1",
            FacturaOrigen = "NV2612345",
            FechaFactura = new DateTime(2026, 10, 14),
            FechaGeneracion = new DateTime(2026, 10, 14, 18, 0, 0),
            FechaActivacion = activacion ?? new DateTime(2026, 10, 14, 18, 0, 0),
            Estado = estado,
            PedidoCanje = pedidoCanje,
            EmpresaCanje = pedidoCanje == null ? null : "1",
            PedidoCanjeTieneLaLinea = pedidoCanjeTieneLaLinea,
            EsPedidoDeLaFacturaOrigen = esPedidoDeLaFactura
        };

        private static Dictionary<string, ProductoParaChequeRegalo> Productos() => RepositorioCanjeChequesRegalo.Diccionario(new[]
        {
            new ProductoParaChequeRegalo { Numero = PRODUCTO_CHEQUE, Nombre = "CHEQUE REGALO 50 € (OCT 2026)", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "COSM", Nombre = "CREMA FACIAL", Grupo = "COS", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "CURSO", Nombre = "CURSO DE MAQUILLAJE", Grupo = "CUR", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "PACK", Nombre = "PACK 26 NAVIDAD AINHOA", Grupo = "COS", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "PELU", Nombre = "TINTE", Grupo = "PEL", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "TiCKET", Nombre = "TICKET DESCUENTO", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" }
        });

        private static LinPedidoVta Linea(string producto, decimal baseImponible, byte tipo = Constantes.TiposLineaVenta.PRODUCTO, short cantidad = 1,
            short estado = Constantes.EstadosLineaVenta.EN_CURSO) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = tipo,
            Producto = producto,
            Cantidad = cantidad,
            Precio = cantidad == 0 ? 0 : baseImponible / cantidad,
            Base_Imponible = baseImponible,
            Estado = estado,
            Picking = 0,
            IVA = "G21"
        };

        private static LinPedidoVta LineaCheque() => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = PRODUCTO_CHEQUE,
            Cantidad = -1,
            Precio = 50,
            Base_Imponible = -50,
            Aplicar_Dto = false,
            Texto = ReglasCanjeChequeRegalo.TextoLinea(Campana()),
            Estado = Constantes.EstadosLineaVenta.EN_CURSO,
            Picking = 0,
            IVA = "G21"
        };

        private Task<PlanCanjeChequeRegalo> Comprobar(IList<LinPedidoVta> lineas, bool pedidoNuevo = true, string cliente = CLIENTE, int pedido = PEDIDO)
            => servicio.Comprobar("1", pedido, cliente, lineas, "G21", null, "carlos", pedidoNuevo);

        private async Task<NestoBusinessException> ComprobarQueFalla(IList<LinPedidoVta> lineas, string codigo, bool pedidoNuevo = true, string cliente = CLIENTE)
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Comprobar(lineas, pedidoNuevo, cliente));
            Assert.AreEqual(codigo, ex.Context.ErrorCode, ex.Message);
            Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
            return ex;
        }

        // ---------------------------------------------------------------- normalización

        [TestMethod]
        public async Task NormalizarLineas_NestoAppMandaMasUno_QuedaMenosUnoConElImporteYSinDescuentos()
        {
            foreach (int cantidad in new[] { 1, 50 })
            {
                var linea = new LineaPedidoVentaDTO
                {
                    tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Producto = "cheque50_oct26 ",
                    Cantidad = cantidad,
                    PrecioUnitario = 1,
                    DescuentoLinea = 0.2M,
                    DescuentoProducto = 0.1M,
                    AplicarDescuento = true,
                    oferta = 3,
                    texto = "lo que sea",
                    iva = null
                };
                var pedido = new PedidoVentaDTO { empresa = "1", iva = "G21", Lineas = new List<LineaPedidoVentaDTO> { linea } };

                await servicio.NormalizarLineas(pedido);

                Assert.AreEqual(PRODUCTO_CHEQUE, linea.Producto);
                Assert.AreEqual(-1, linea.Cantidad, $"con {cantidad}");
                Assert.AreEqual(50M, linea.PrecioUnitario);
                Assert.AreEqual(0M, linea.DescuentoLinea);
                Assert.AreEqual(0M, linea.DescuentoProducto);
                Assert.IsFalse(linea.AplicarDescuento);
                Assert.IsNull(linea.oferta);
                Assert.AreEqual("G21", linea.iva, "el IVA del producto");
                Assert.AreEqual("Cheque regalo 50 € (campaña CHEQUE50_OCT_2026)", linea.texto);
                Assert.AreEqual(-50M, linea.BaseImponible);
            }
        }

        [TestMethod]
        public async Task NormalizarLineas_SinCampanas_NoTocaNada()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo>());
            var linea = new LineaPedidoVentaDTO { tipoLinea = 1, Producto = PRODUCTO_CHEQUE, Cantidad = 1, PrecioUnitario = 1 };

            await servicio.NormalizarLineas(new PedidoVentaDTO { empresa = "1", Lineas = new List<LineaPedidoVentaDTO> { linea } });

            Assert.AreEqual(1, linea.Cantidad);
        }

        [TestMethod]
        public async Task Comprobar_LaLineaCreadaConOtrosValores_SeNormalizaYSeRecalcula()
        {
            LinPedidoVta cheque = LineaCheque();
            cheque.Cantidad = 50;
            cheque.Precio = 1;
            cheque.Descuento = 0.1M;
            cheque.Aplicar_Dto = true;
            int recalculos = 0;

            await servicio.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("COSM", 300), cheque }, "G21",
                l => recalculos++, "carlos", pedidoNuevo: true);

            Assert.AreEqual((short)-1, cheque.Cantidad);
            Assert.AreEqual(50M, cheque.Precio);
            Assert.AreEqual(0M, cheque.Descuento);
            Assert.IsFalse(cheque.Aplicar_Dto);
            Assert.AreEqual(1, recalculos);
        }

        [TestMethod]
        public async Task Comprobar_LaLineaYaConPicking_NoSeToca()
        {
            LinPedidoVta cheque = LineaCheque();
            cheque.Picking = 99001;
            cheque.Texto = "otro";
            int recalculos = 0;
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: PEDIDO, pedidoCanjeTieneLaLinea: true) });

            await servicio.Comprobar("1", PEDIDO, CLIENTE, new List<LinPedidoVta> { Linea("COSM", 300), cheque }, "G21",
                l => recalculos++, "carlos", pedidoNuevo: false);

            Assert.AreEqual("otro", cheque.Texto);
            Assert.AreEqual(0, recalculos);
        }

        // ---------------------------------------------------------------- reglas

        [TestMethod]
        public async Task Comprobar_SinCampanas_NoHaceNada()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo>());

            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { LineaCheque() }, pedidoNuevo: false);

            Assert.IsFalse(plan.HayQueTocarCheques);
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_PedidoNuevoSinCheque_NoToca()
        {
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 100) });

            Assert.IsFalse(plan.HayQueTocarCheques);
        }

        [TestMethod]
        public async Task Comprobar_ConChequeValido_LoReserva()
        {
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() });

            Assert.AreEqual(7, plan.ChequeAReservar.Id);
            Assert.IsFalse(plan.LiberarOtros, "un pedido nuevo no tiene nada que soltar");
            A.CallTo(() => repositorio.LeerChequesDelCliente("1", CLIENTE, PEDIDO)).MustHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_ClienteSinCheque_Falla()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).Returns(new List<ChequeRegaloCliente>());

            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE);

            StringAssert.Contains(ex.Message, CLIENTE);
        }

        [TestMethod]
        public async Task Comprobar_ClienteGenerico_FallaSinMirarLaBD()
        {
            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE, cliente: "31517");

            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_ChequeDeOtraCampana_Falla()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo>
            {
                Campana(),
                Campana("CHEQUE50_FEB_2027", "CHEQUE50_FEB27")
            });
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(campana: "CHEQUE50_FEB_2027") });

            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_OTRA_CAMPANA);

            StringAssert.Contains(ex.Message, "CHEQUE50_FEB27");
        }

        [TestMethod]
        public async Task Comprobar_ChequeYaUsadoEnOtroPedido_Falla()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: 929999, pedidoCanjeTieneLaLinea: true) });

            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_YA_USADO);

            StringAssert.Contains(ex.Message, "929999");
        }

        [TestMethod]
        public async Task Comprobar_ChequeEnUnPedidoQueYaNoTieneLaLinea_EstaLibre()
        {
            // El pedido o la línea se borraron fuera de la API: el cheque se cura al volver a usarlo
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: 929999, pedidoCanjeTieneLaLinea: false) });

            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() });

            Assert.IsNotNull(plan.ChequeAReservar);
        }

        [TestMethod]
        public async Task Comprobar_FueraDePlazo_FallaDesdeElDiaSiguienteALaFechaLimite()
        {
            ahora = new DateTime(2026, 11, 7, 23, 59, 0);
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() });
            Assert.IsNotNull(plan.ChequeAReservar, "el 7 de noviembre todavía vale");

            ahora = new DateTime(2026, 11, 8, 0, 1, 0);
            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO);
            StringAssert.Contains(ex.Message, "07/11/2026");
        }

        [TestMethod]
        public async Task Comprobar_ChequeYaEnEstePedido_SeConservaAunqueHayaPasadoLaFechaLimite()
        {
            ahora = new DateTime(2026, 11, 20);
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: PEDIDO, pedidoCanjeTieneLaLinea: true) });

            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, pedidoNuevo: false);

            Assert.AreEqual(7, plan.ChequeAReservar.Id);
        }

        [TestMethod]
        public async Task Comprobar_ChequeYaEnEstePedido_ElMinimoSeVuelveAMirar()
        {
            // Quitar producto en una modificación no puede dejar el cheque en un pedido que ya no llega
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: PEDIDO, pedidoCanjeTieneLaLinea: true) });

            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 200), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO, pedidoNuevo: false);
        }

        [TestMethod]
        public async Task Comprobar_CampanaApagada_Falla()
        {
            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { Campana(activa: false) });

            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, ReglasCanjeChequeRegalo.ERROR_CAMPANA_INACTIVA);
        }

        [TestMethod]
        public async Task Comprobar_ChequeAnuladoOCaducado_Falla()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Anulado") });
            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, ReglasCanjeChequeRegalo.ERROR_ANULADO);

            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Caducado") });
            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO);
        }

        [TestMethod]
        public async Task Comprobar_ChequeSinActivar_Falla()
        {
            var sinActivar = Cheque();
            sinActivar.FechaActivacion = null;
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).Returns(new List<ChequeRegaloCliente> { sinActivar });

            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, ReglasCanjeChequeRegalo.ERROR_NO_ACTIVADO);
        }

        [TestMethod]
        public async Task Comprobar_EnElPedidoDeLaFacturaQueLoGenero_Falla()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(esPedidoDeLaFactura: true) });

            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_MISMO_PEDIDO, pedidoNuevo: false);

            StringAssert.Contains(ex.Message, "otro pedido");
        }

        [TestMethod]
        public async Task Comprobar_DosLineasDeCheque_Falla()
        {
            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 600), LineaCheque(), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_VARIAS_LINEAS);
        }

        // ---------------------------------------------------------------- mínimo

        [TestMethod]
        public async Task Comprobar_JustoElMinimo_NoVale_DiceCuantoLlevaYCuantoFalta()
        {
            NestoBusinessException ex = await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 250), LineaCheque() },
                ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO);

            StringAssert.Contains(ex.Message, "Lleva 250,00 €");
            StringAssert.Contains(ex.Message, "faltan 0,01 €");
            Assert.AreEqual(250M, ex.Context.AdditionalData["baseComputable"]);
            Assert.AreEqual(0.01M, ex.Context.AdditionalData["falta"]);
        }

        [TestMethod]
        public async Task Comprobar_UnCentimoPorEncimaDelMinimo_Vale()
        {
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 250.01M), LineaCheque() });

            Assert.IsNotNull(plan.ChequeAReservar);
        }

        [TestMethod]
        public async Task Comprobar_CuentasContablesFicticiosPack26YPeluqueria_NoSuman()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea("COSM", 200),
                Linea("62400003", 100, tipo: Constantes.TiposLineaVenta.CUENTA_CONTABLE),  // portes
                Linea("TiCKET", 100),                                                     // ficticio
                Linea("PACK", 100),                                                       // PACK 26
                Linea("PELU", 100),                                                       // grupo PEL
                Linea("NOEXISTE", 100),                                                   // sin ficha: no suma
                LineaCheque()
            };

            NestoBusinessException ex = await ComprobarQueFalla(lineas, ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO);

            StringAssert.Contains(ex.Message, "Lleva 200,00 €");
            StringAssert.Contains(ex.Message, "«PACK 26»");
            StringAssert.Contains(ex.Message, "grupo PEL");
        }

        [TestMethod]
        public async Task Comprobar_CursosYLineasYaServidas_Suman()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea("CURSO", 150),
                Linea("COSM", 110, estado: Constantes.EstadosLineaVenta.FACTURA),
                LineaCheque()
            };

            PlanCanjeChequeRegalo plan = await Comprobar(lineas, pedidoNuevo: false);

            Assert.IsNotNull(plan.ChequeAReservar);
        }

        [TestMethod]
        public void BaseComputable_DespuesDeDescuentos()
        {
            LinPedidoVta conDescuento = Linea("COSM", 270);  // base ya con el descuento aplicado
            decimal baseComputable = ReglasCanjeChequeRegalo.BaseComputable(new[] { conDescuento, LineaCheque() }, Productos(), Campana());

            Assert.AreEqual(270M, baseComputable);
        }

        // ---------------------------------------------------------------- reserva, liberación y concurrencia

        [TestMethod]
        public async Task Reservar_SiOtroPedidoLoAcabaDeCoger_FallaConYaUsado()
        {
            A.CallTo(() => repositorio.ReservarCheque(7, "1", PEDIDO, "carlos")).Returns(false);
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() });

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.Reservar(plan));

            Assert.AreEqual(ReglasCanjeChequeRegalo.ERROR_YA_USADO, ex.Context.ErrorCode);
        }

        [TestMethod]
        public async Task Reservar_Libre_ReservaParaEstePedido()
        {
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() });

            await servicio.Reservar(plan);

            A.CallTo(() => repositorio.ReservarCheque(7, "1", PEDIDO, "carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Modificacion_QueQuitaLaLineaDelCheque_LoSuelta()
        {
            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300) }, pedidoNuevo: false);

            Assert.IsTrue(plan.HayQueTocarCheques);
            Assert.IsNull(plan.ChequeAReservar);
            await servicio.Reservar(plan);
            await servicio.LiberarOtros(plan);

            A.CallTo(() => repositorio.ReservarCheque(A<int>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LiberarChequesDelPedido(PEDIDO, 0, "carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Modificacion_QueCambiaDeCliente_SueltaElDelAnteriorYReservaElDelNuevo()
        {
            // El cheque 7 era del cliente anterior y estaba en este pedido; el nuevo cliente tiene el 8
            A.CallTo(() => repositorio.LeerChequesDelCliente("1", "20000", PEDIDO))
                .Returns(new List<ChequeRegaloCliente> { Cheque(id: 8) });

            PlanCanjeChequeRegalo plan = await Comprobar(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, pedidoNuevo: false, cliente: "20000");
            await servicio.Reservar(plan);
            await servicio.LiberarOtros(plan);

            A.CallTo(() => repositorio.ReservarCheque(8, "1", PEDIDO, "carlos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LiberarChequesDelPedido(PEDIDO, 8, "carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Modificacion_QueCambiaAUnClienteSinCheque_Falla()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente("1", "20000", PEDIDO)).Returns(new List<ChequeRegaloCliente>());

            await ComprobarQueFalla(new List<LinPedidoVta> { Linea("COSM", 300), LineaCheque() }, ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE,
                pedidoNuevo: false, cliente: "20000");
        }

        [TestMethod]
        public async Task BorrarPedido_SueltaSuCheque()
        {
            await servicio.LiberarPedido(PEDIDO, "carlos");

            A.CallTo(() => repositorio.LiberarChequesDelPedido(PEDIDO, 0, "carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void SqlReservar_EsUnUpdateCondicionado()
        {
            string sql = RepositorioCanjeChequesRegalo.SQL_RESERVAR;

            StringAssert.Contains(sql, "WITH (UPDLOCK, ROWLOCK)");
            StringAssert.Contains(sql, "c.Id = @p0");
            StringAssert.Contains(sql, "c.Estado IN ('Generado', 'Canjeado')");
            StringAssert.Contains(sql, "c.PedidoCanje IS NULL");
            StringAssert.Contains(sql, "OR c.PedidoCanje = @p2");
            StringAssert.Contains(sql, "OR NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto)");
        }

        [TestMethod]
        public void SqlLiberar_NoSueltaElQueSeReservaNiElYaServido()
        {
            string sql = RepositorioCanjeChequesRegalo.SQL_LIBERAR;

            StringAssert.Contains(sql, "c.PedidoCanje = @p0 AND c.Estado = 'Canjeado' AND c.Id <> @p1");
            StringAssert.Contains(sql, "l.Estado >= 2");
            StringAssert.Contains(sql, "SET Estado = 'Generado', EmpresaCanje = NULL, PedidoCanje = NULL, FechaCanje = NULL");
        }

        [TestMethod]
        public void SqlCampanas_SinLasColumnasDeC4_SigueReconociendoLasCampanas()
        {
            string sql = RepositorioCanjeChequesRegalo.SQL_CAMPANAS;

            StringAssert.Contains(sql, "OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL");
            StringAssert.Contains(sql, "COL_LENGTH('dbo.ChequesRegaloCampanas', 'GruposExcluidosMinimo') IS NULL");
            StringAssert.Contains(sql, "EXEC sp_executesql");
        }

        [TestMethod]
        public async Task LeerProductos_DeLaFichaSinEspacios()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Producto> productos = A.Fake<DbSet<Producto>>(o => o.Implements(typeof(IQueryable<Producto>)).Implements(typeof(IDbAsyncEnumerable<Producto>)));
            ConfigurarFakeDbSet(productos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "PELU", Nombre = "TINTE   ", Grupo = "PEL", Ficticio = false, IVA_Repercutido = "G21 " },
                new Producto { Empresa = "1", Número = PRODUCTO_CHEQUE, Nombre = "CHEQUE", Grupo = "COS", Ficticio = true, IVA_Repercutido = "G21" },
                new Producto { Empresa = "3", Número = "PELU", Nombre = "OTRA EMPRESA", Grupo = "XXX" }
            }.AsQueryable());
            A.CallTo(() => db.Productos).Returns(productos);

            Dictionary<string, ProductoParaChequeRegalo> leidos = await new RepositorioCanjeChequesRegalo(db).LeerProductos("1", new[] { "PELU ", PRODUCTO_CHEQUE });

            Assert.AreEqual(2, leidos.Count);
            Assert.AreEqual("TINTE", leidos["pelu"].Nombre);
            Assert.AreEqual("PEL", leidos["PELU"].Grupo);
            Assert.AreEqual("G21", leidos["PELU"].IvaRepercutido);
            Assert.IsTrue(leidos[PRODUCTO_CHEQUE].Ficticio);
        }

        // ---------------------------------------------------------------- consulta para los clientes

        [TestMethod]
        public async Task LeerChequeVigente_Disponible()
        {
            ChequeRegaloClienteDTO dto = await servicio.LeerChequeVigente("1", CLIENTE, HOY);

            Assert.AreEqual("Disponible", dto.Estado);
            Assert.IsTrue(dto.SePuedeUsar);
            Assert.AreEqual(PRODUCTO_CHEQUE, dto.Producto);
            Assert.AreEqual(50M, dto.Importe);
            Assert.AreEqual(250M, dto.MinimoCanje);
            Assert.AreEqual(new DateTime(2026, 11, 7), dto.CanjeHasta);
            Assert.AreEqual("NV2612345", dto.FacturaOrigen);
            CollectionAssert.AreEqual(new[] { "PACK 26" }, dto.PrefijosNombreExcluidosMinimo);
            CollectionAssert.AreEqual(new[] { "PEL" }, dto.GruposExcluidosMinimo);
            Assert.IsNull(dto.PedidoCanje);
            StringAssert.Contains(dto.Mensaje, "07/11/2026");
        }

        [TestMethod]
        public async Task LeerChequeVigente_EnUnPedido()
        {
            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._))
                .Returns(new List<ChequeRegaloCliente> { Cheque(estado: "Canjeado", pedidoCanje: 929999, pedidoCanjeTieneLaLinea: true) });

            ChequeRegaloClienteDTO dto = await servicio.LeerChequeVigente("1", CLIENTE, HOY);

            Assert.AreEqual("EnPedido", dto.Estado);
            Assert.IsFalse(dto.SePuedeUsar);
            Assert.AreEqual(929999, dto.PedidoCanje);
        }

        [TestMethod]
        public async Task LeerChequeVigente_SinCampanaActivaOGenerico_Nada()
        {
            Assert.IsNull(await servicio.LeerChequeVigente("1", "31517", HOY), "genérico");

            A.CallTo(() => repositorio.LeerCampanas()).Returns(new List<CampanaCanjeChequeRegalo> { Campana(activa: false) });
            Assert.IsNull(await new ServicioCanjeChequesRegalo(repositorio).LeerChequeVigente("1", CLIENTE, HOY), "campaña apagada");
        }

        [TestMethod]
        public async Task ControllerChequesRegalo_DevuelveElChequeO404()
        {
            var controller = new ChequesRegaloController(A.Fake<NVEntities>()) { ServicioCanje = servicio, Ahora = () => HOY };

            var ok = await controller.GetChequeCliente(CLIENTE) as OkNegotiatedContentResult<ChequeRegaloClienteDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(CAMPANA, ok.Content.Campana);

            A.CallTo(() => repositorio.LeerChequesDelCliente(A<string>._, A<string>._, A<int>._)).Returns(new List<ChequeRegaloCliente>());
            Assert.IsInstanceOfType(await controller.GetChequeCliente(CLIENTE), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task ControllerChequesRegalo_UnClienteDeLaTiendaSoloVeElSuyo()
        {
            var controller = new ChequesRegaloController(A.Fake<NVEntities>())
            {
                ServicioCanje = servicio,
                RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", "20000") }, "JWT")) }
            };

            var resultado = await controller.GetChequeCliente(CLIENTE) as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task PedidosCliente_ConLaLineaDelCheque_NoSeCreaTodavia()
        {
            // El canje en la app de clientas y en la tienda online va aparte (febrero)
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", CLIENTE) }, "JWT")) },
                LeerProductosChequeRegalo = () => Task.FromResult(new List<string> { PRODUCTO_CHEQUE })
            };
            var peticion = new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest>
                {
                    new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 },
                    new LineaPedidoClienteRequest { Producto = "cheque50_oct26", Cantidad = 1 }
                }
            };

            var resultado = await controller.PostPedidoCliente(peticion) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(PedidosClienteController.MENSAJE_CHEQUE_REGALO_NO_DISPONIBLE, resultado.Message);
        }

        [TestMethod]
        public async Task PedidosCliente_SinLaLineaDelCheque_NoLoBloquea()
        {
            var controller = new PedidosClienteController(A.Fake<NVEntities>(), A.Fake<IServicioPagos>())
            {
                LeerProductosChequeRegalo = () => Task.FromResult(new List<string> { PRODUCTO_CHEQUE })
            };

            Assert.IsFalse(await controller.LlevaChequeRegalo(new PedidoClienteRequest
            {
                Lineas = new List<LineaPedidoClienteRequest> { new LineaPedidoClienteRequest { Producto = "12345", Cantidad = 1 } }
            }));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
