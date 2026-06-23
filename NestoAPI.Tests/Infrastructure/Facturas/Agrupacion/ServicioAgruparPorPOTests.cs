using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#195 (Fase 3): tests del orquestador <see cref="ServicioAgruparPorPO"/>.
    /// Usan la estrategia y el motor REALES contra DbSets falsos, y solo simulan
    /// <see cref="IServicioFacturas"/> para verificar la orquestación (qué pedido se factura)
    /// sin ejecutar el SP.
    /// </summary>
    [TestClass]
    public class ServicioAgruparPorPOTests
    {
        private NVEntities db;
        private IServicioFacturas servicioFacturas;
        private ServicioAgruparPorPO servicio;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioFacturas = A.Fake<IServicioFacturas>();
            ConfigurarDbSet<PedidosEspeciale>(s => A.CallTo(() => db.PedidosEspeciales).Returns(s), new List<PedidosEspeciale>());
            ConfigurarDbSet<Ubicacion>(s => A.CallTo(() => db.Ubicaciones).Returns(s), new List<Ubicacion>());

            servicio = new ServicioAgruparPorPO(
                db,
                new EstrategiaAgrupacionPO(db),
                new MotorAgrupacionPedidos(db),
                servicioFacturas);
        }

        [TestMethod]
        public async Task EvaluarYProcesar_GrupoPOCompleto_AgrupaYFacturaConPOConservado()
        {
            CabPedidoVta principal = NuevoPedido(100, "CLI-1", "0", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 1);
            CabPedidoVta hermano = NuevoPedido(200, "CLI-1", "5", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 2);
            ConfigurarCabPedidos(principal, hermano);
            ConfigurarClientePrincipal("CLI-1", "0");
            List<int> facturados = CapturarPedidosFacturados();

            ResultadoAgrupacionPO resultado = await servicio.EvaluarYProcesar("1", "usuario");

            // Factura el destino (el del contacto principal "0" = pedido 100).
            Assert.AreEqual(100, facturados.Single());
            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.AreEqual(0, resultado.Errores.Count);
            // El destino conserva el PO (prdCrearFacturaVta lo copiará a la factura).
            Assert.AreEqual("PO-1", principal.SuPedido);
            // Las dos líneas acaban en el destino.
            Assert.AreEqual(2, principal.LinPedidoVtas.Count);
            Assert.IsTrue(principal.Agrupada);
        }

        [TestMethod]
        public async Task EvaluarYProcesar_GrupoPOIncompleto_NoAgrupa()
        {
            CabPedidoVta completo = NuevoPedido(100, "CLI-1", "0", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 1);
            // El hermano tiene una línea aún pendiente: el grupo NO está listo.
            CabPedidoVta incompleto = NuevoPedido(200, "CLI-1", "5", "PO-1", Constantes.EstadosLineaVenta.PENDIENTE, orden: 2);
            ConfigurarCabPedidos(completo, incompleto);
            ConfigurarClientePrincipal("CLI-1", "0");
            List<int> facturados = CapturarPedidosFacturados();

            ResultadoAgrupacionPO resultado = await servicio.EvaluarYProcesar("1", "usuario");

            Assert.AreEqual(0, facturados.Count, "No debe facturar si el grupo no está completo.");
            Assert.AreEqual(0, resultado.Facturas.Count);
            Assert.IsFalse(completo.Agrupada);
        }

        [TestMethod]
        public async Task EvaluarYProcesar_PreservaSuPedidoEnPedidoFacturado()
        {
            CabPedidoVta principal = NuevoPedido(100, "CLI-1", "0", "PO-XYZ", Constantes.EstadosLineaVenta.ALBARAN, orden: 1);
            CabPedidoVta hermano = NuevoPedido(200, "CLI-1", "5", "PO-XYZ", Constantes.EstadosLineaVenta.ALBARAN, orden: 2);
            ConfigurarCabPedidos(principal, hermano);
            ConfigurarClientePrincipal("CLI-1", "0");

            CabPedidoVta facturado = null;
            A.CallTo(() => servicioFacturas.CrearFactura("1", A<int>._, A<string>._, A<string>._))
                .Invokes((string e, int p, string u, string ua) =>
                    facturado = new[] { principal, hermano }.SingleOrDefault(x => x.Número == p))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { NumeroFactura = "FAC-1" }));

            _ = await servicio.EvaluarYProcesar("1", "usuario");

            Assert.IsNotNull(facturado);
            Assert.AreEqual("PO-XYZ", facturado.SuPedido);
        }

        [TestMethod]
        public async Task EvaluarYProcesar_GrupoDeUnSoloPedido_NoAgrupaNiFactura()
        {
            // Un único pedido con PO: lo factura el flujo normal, el orquestador lo ignora.
            CabPedidoVta unico = NuevoPedido(100, "CLI-1", "0", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 1);
            ConfigurarCabPedidos(unico);
            ConfigurarClientePrincipal("CLI-1", "0");
            List<int> facturados = CapturarPedidosFacturados();

            ResultadoAgrupacionPO resultado = await servicio.EvaluarYProcesar("1", "usuario");

            Assert.AreEqual(0, facturados.Count);
            Assert.AreEqual(0, resultado.Facturas.Count);
            Assert.IsFalse(unico.Agrupada);
        }

        [TestMethod]
        public async Task EvaluarYProcesar_FacturaFalla_AislaErrorYSiguenLosDemas()
        {
            // PO-1 (cliente CLI-1) fallará al facturar; PO-2 (cliente CLI-2) debe facturarse igual.
            ConfigurarCabPedidos(
                NuevoPedido(100, "CLI-1", "0", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 1),
                NuevoPedido(200, "CLI-1", "5", "PO-1", Constantes.EstadosLineaVenta.ALBARAN, orden: 2),
                NuevoPedido(300, "CLI-2", "0", "PO-2", Constantes.EstadosLineaVenta.ALBARAN, orden: 3),
                NuevoPedido(400, "CLI-2", "5", "PO-2", Constantes.EstadosLineaVenta.ALBARAN, orden: 4));
            ConfigurarClientes(
                (cliente: "CLI-1", contacto: "0"),
                (cliente: "CLI-2", contacto: "0"));

            A.CallTo(() => servicioFacturas.CrearFactura("1", 100, A<string>._, A<string>._))
                .Throws(new InvalidOperationException("descuadre"));
            A.CallTo(() => servicioFacturas.CrearFactura("1", 300, A<string>._, A<string>._))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { NumeroFactura = "FAC-2" }));

            ResultadoAgrupacionPO resultado = await servicio.EvaluarYProcesar("1", "usuario");

            Assert.AreEqual(1, resultado.Facturas.Count);
            Assert.AreEqual("FAC-2", resultado.Facturas[0].NumeroFactura);
            Assert.AreEqual(1, resultado.Errores.Count);
            Assert.AreEqual("PO-1", resultado.Errores[0].SuPedido);
            Assert.AreEqual("descuadre", resultado.Errores[0].Mensaje);
        }

        // ----- helpers -----

        // Devuelve una lista (capturada por referencia) con los números de pedido que se
        // mandan a facturar; se rellena durante EvaluarYProcesar y se lee después.
        private List<int> CapturarPedidosFacturados()
        {
            var facturados = new List<int>();
            A.CallTo(() => servicioFacturas.CrearFactura("1", A<int>._, A<string>._, A<string>._))
                .Invokes((string e, int p, string u, string ua) => facturados.Add(p))
                .Returns(Task.FromResult(new CrearFacturaResponseDTO { NumeroFactura = "FAC" }));
            return facturados;
        }

        private void ConfigurarCabPedidos(params CabPedidoVta[] pedidos)
        {
            ConfigurarDbSet<CabPedidoVta>(s => A.CallTo(() => db.CabPedidoVtas).Returns(s), pedidos.ToList());
        }

        private void ConfigurarClientePrincipal(string cliente, string contactoPrincipal)
        {
            ConfigurarClientes((cliente, contactoPrincipal));
        }

        private void ConfigurarClientes(params (string cliente, string contacto)[] principales)
        {
            var clientes = new List<Cliente>();
            foreach ((string cliente, string contacto) in principales)
            {
                clientes.Add(new Cliente { Empresa = "1", Nº_Cliente = cliente, Contacto = contacto, ClientePrincipal = true });
            }
            ConfigurarDbSet<Cliente>(s => A.CallTo(() => db.Clientes).Returns(s), clientes);
        }

        private static CabPedidoVta NuevoPedido(int numero, string cliente, string contacto,
            string suPedido, short estadoLinea, int orden)
        {
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = numero,
                Nº_Cliente = cliente,
                Contacto = contacto,
                SuPedido = suPedido,
                MantenerJunto = true,
                Agrupada = false,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta
                    {
                        Empresa = "1",
                        Número = numero,
                        Nº_Orden = orden,
                        Contacto = contacto,
                        Estado = estadoLinea,
                        VtoBueno = true
                    }
                }
            };
            foreach (LinPedidoVta l in pedido.LinPedidoVtas)
            {
                l.CabPedidoVta = pedido;
            }
            return pedido;
        }

        private static void ConfigurarDbSet<T>(Action<DbSet<T>> asignar, List<T> data) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            IQueryable<T> query = data.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(query.Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(query.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(query.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).Returns(query.GetEnumerator());
            asignar(set);
        }
    }
}
