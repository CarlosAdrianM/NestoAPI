using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using static NestoAPI.Tests.Infrastructure.Facturas.Agrupacion.EstrategiaAgrupacionFinDeMesTests;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: tests del orquestador del fin de mes. Estrategia y motor REALES contra DbSets falsos; solo se simula
    /// <see cref="IServicioFacturas"/> (no se ejecuta el SP).
    /// </summary>
    [TestClass]
    public class ServicioFacturarFinDeMesTests
    {
        private static readonly DateTime CORTE = new DateTime(2026, 10, 31);
        private static readonly DateTime EN_OCTUBRE = new DateTime(2026, 10, 15);
        private static readonly DateTime EN_NOVIEMBRE = new DateTime(2026, 11, 2);

        private NVEntities db;
        private IServicioFacturas servicioFacturas;
        private ServicioFacturarFinDeMes servicio;
        private List<Modificacion> modificaciones;
        private List<int> facturados;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioFacturas = A.Fake<IServicioFacturas>();
            modificaciones = new List<Modificacion>();
            ConfigurarDbSet(s => A.CallTo(() => db.PedidosEspeciales).Returns(s), new List<PedidosEspeciale>());
            ConfigurarDbSet(s => A.CallTo(() => db.Ubicaciones).Returns(s), new List<Ubicacion>());
            ConfigurarDbSet(s => A.CallTo(() => db.Modificaciones).Returns(s), modificaciones);
            ConfigurarDbSet(s => A.CallTo(() => db.Clientes).Returns(s), new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "CLI-1", Contacto = "0", ClientePrincipal = true },
                new Cliente { Empresa = "1", Nº_Cliente = "CLI-2", Contacto = "0", ClientePrincipal = true }
            });

            facturados = new List<int>();
            A.CallTo(() => servicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, int p, string u, string ua) =>
                {
                    facturados.Add(p);
                    return Task.FromResult(new CrearFacturaResponseDTO { Empresa = e, NumeroPedido = p, NumeroFactura = "NV26" + p });
                });

            servicio = new ServicioFacturarFinDeMes(db, new EstrategiaAgrupacionFinDeMes(db), new MotorAgrupacionPedidos(db), servicioFacturas)
            {
                ClienteExigeSuPedido = (e, c) => false
            };
        }

        [TestMethod]
        public async Task Facturar_VariosContactos_LosReuneEnElDelPrincipalYLoFactura()
        {
            CabPedidoVta contacto31 = Pedido(100, "CLI-1", "31", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta principal = Pedido(200, "CLI-1", "0", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta contacto51 = Pedido(300, "CLI-1", "51", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            ConfigurarCabPedidos(contacto31, principal, contacto51);

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "NUEVAVISION\\Admin");

            CollectionAssert.AreEqual(new[] { 200 }, facturados);
            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.AreEqual("NV26200", resultado.Facturas[0].NumeroFactura);
            Assert.AreEqual(200, resultado.Facturas[0].PedidoDestino);
            CollectionAssert.AreEqual(new[] { 100, 200, 300 }, resultado.Facturas[0].PedidosReunidos.ToArray());
            Assert.AreEqual(2, resultado.Facturas[0].LineasMovidas);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, principal.LinPedidoVtas.Select(l => l.Nº_Orden).ToArray());
            Assert.IsTrue(principal.LinPedidoVtas.All(l => l.Número == 200));
            CollectionAssert.AreEquivalent(new[] { "31", "0", "51" }, principal.LinPedidoVtas.Select(l => l.Contacto).ToArray(),
                "cada línea conserva su dirección de entrega");
            Assert.IsTrue(principal.Agrupada, "sin la marca CrearFactura contestaría «FDM» sin facturar");
            Assert.AreEqual(0, resultado.Errores.Count);
            Assert.AreEqual(0, resultado.Saltados.Count);
        }

        [TestMethod]
        public async Task Facturar_DejaUnaFilaEnModificaciones()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-1", "5", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            _ = await servicio.Facturar("1", CORTE, null, null, "NUEVAVISION\\Admin");

            Modificacion fila = modificaciones.Single();
            Assert.AreEqual("Pedidos", fila.Tabla);
            Assert.AreEqual("NUEVAVISION\\Admin", fila.Usuario);
            StringAssert.Contains(fila.Anterior, "\"Pedido\":200");
            StringAssert.Contains(fila.Nuevo, "\"PedidoDestino\":100");
            StringAssert.Contains(fila.Nuevo, "\"PedidosReunidos\":[100,200]");
        }

        [TestMethod]
        public async Task Facturar_AlbaranesDeNoviembreYPendientes_SeQuedanEnSuPedido()
        {
            CabPedidoVta principal = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta mixto = Pedido(200, "CLI-1", "5",
                Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE),
                Linea(4, Constantes.EstadosLineaVenta.PENDIENTE, null));
            ConfigurarCabPedidos(principal, mixto);

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            CollectionAssert.AreEqual(new[] { 100 }, facturados);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, principal.LinPedidoVtas.Select(l => l.Nº_Orden).ToArray());
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, mixto.LinPedidoVtas.Select(l => l.Nº_Orden).ToArray());
            Assert.IsFalse(mixto.Agrupada);
            Assert.AreEqual(1, resultado.Facturas[0].LineasMovidas);
        }

        [TestMethod]
        public async Task Facturar_UnSoloPedido_TambienSeFactura()
        {
            CabPedidoVta unico = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            ConfigurarCabPedidos(unico);

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            CollectionAssert.AreEqual(new[] { 100 }, facturados);
            Assert.IsTrue(unico.Agrupada);
            Assert.AreEqual(1, resultado.Facturas.Count);
        }

        [TestMethod]
        public async Task Facturar_ErrorAlFacturarUnCliente_NoParaALosDemas()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-1", "5", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(300, "CLI-2", "0", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));
            A.CallTo(() => servicioFacturas.CrearFactura("1", 100, A<string>._, A<string>._))
                .Throws(new InvalidOperationException("descuadre"));

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.AreEqual("CLI-2", resultado.Facturas[0].Cliente);
            ErrorFinDeMes error = resultado.Errores.Single();
            Assert.AreEqual("CLI-1", error.Cliente);
            Assert.AreEqual(100, error.PedidoDestino, "los albaranes ya están reunidos ahí: se dice dónde");
            StringAssert.Contains(error.Mensaje, "descuadre");
            StringAssert.Contains(error.Mensaje, "100");
        }

        [TestMethod]
        public async Task Facturar_CrearFacturaContestaFDM_EsError()
        {
            ConfigurarCabPedidos(Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));
            A.CallTo(() => servicioFacturas.CrearFactura("1", 100, A<string>._, A<string>._))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { NumeroFactura = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES }));

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            Assert.AreEqual(0, resultado.Facturas.Count);
            Assert.AreEqual(1, resultado.Errores.Count);
        }

        [TestMethod]
        public async Task Facturar_ClienteQueExigePO_SeSaltaSinTocarNada()
        {
            CabPedidoVta a = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta b = Pedido(200, "CLI-1", "5", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            ConfigurarCabPedidos(a, b);
            servicio.ClienteExigeSuPedido = (e, c) => c == "CLI-1";

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            Assert.AreEqual(0, facturados.Count);
            Assert.AreEqual(1, resultado.Saltados.Count);
            StringAssert.Contains(resultado.Saltados[0].Motivo, "P.O.");
            Assert.IsFalse(a.Agrupada);
            Assert.AreEqual(1, b.LinPedidoVtas.Count);
            Assert.AreEqual(0, modificaciones.Count);
        }

        [TestMethod]
        public async Task Facturar_PedidoSinIva_SeSaltaElClienteSinTocarNada()
        {
            CabPedidoVta conIva = Pedido(100, "CLI-1", "5", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta sinIva = Pedido(200, "CLI-1", "0", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            sinIva.IVA = null;
            ConfigurarCabPedidos(conIva, sinIva,
                Pedido(300, "CLI-2", "0", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            CollectionAssert.AreEqual(new[] { 300 }, facturados);
            StringAssert.Contains(resultado.Saltados.Single().Motivo, "200");
            Assert.AreEqual("5", conIva.Contacto, "no se le cambia el contacto a un cliente saltado");
            Assert.IsFalse(conIva.Agrupada);
        }

        [TestMethod]
        public async Task Facturar_TodosConAlbaranesPosteriores_SeSalta()
        {
            CabPedidoVta mixto = Pedido(100, "CLI-1", "0",
                Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE));
            ConfigurarCabPedidos(mixto);

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            Assert.AreEqual(0, facturados.Count);
            Assert.AreEqual(1, resultado.Saltados.Count);
            Assert.IsFalse(mixto.Agrupada);
        }

        [TestMethod]
        public async Task Facturar_DestinoConLineasPendientes_LeQuitaLaMarcaDeAgrupadoTrasFacturar()
        {
            CabPedidoVta principal = Pedido(100, "CLI-1", "0",
                Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                Linea(2, Constantes.EstadosLineaVenta.PENDIENTE, null));
            ConfigurarCabPedidos(principal, Pedido(200, "CLI-1", "5", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));
            bool agrupadaAlFacturar = false;
            A.CallTo(() => servicioFacturas.CrearFactura("1", 100, A<string>._, A<string>._))
                .ReturnsLazily(() =>
                {
                    agrupadaAlFacturar = principal.Agrupada;
                    return Task.FromResult(new CrearFacturaResponseDTO { Empresa = "1", NumeroFactura = "NV26100" });
                });

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, null, null, "usuario");

            Assert.IsTrue(agrupadaAlFacturar);
            Assert.IsFalse(principal.Agrupada, "lo que queda por servir se factura en su fin de mes, no al albaranarlo");
            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.IsNull(resultado.Facturas[0].Aviso);
        }

        [TestMethod]
        public async Task Facturar_PasaClienteYContactoALaEstrategia()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-2", "0", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            ResultadoFacturacionFinDeMes resultado = await servicio.Facturar("1", CORTE, "CLI-2", null, "usuario");

            CollectionAssert.AreEqual(new[] { 200 }, facturados);
            Assert.AreEqual(CORTE, resultado.FechaCorte);
        }

        private void ConfigurarCabPedidos(params CabPedidoVta[] pedidos)
        {
            ConfigurarDbSet(s => A.CallTo(() => db.CabPedidoVtas).Returns(s), pedidos.ToList());
        }
    }
}
