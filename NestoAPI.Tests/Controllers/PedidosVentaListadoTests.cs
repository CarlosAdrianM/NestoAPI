using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#496: GET api/PedidosVenta (listado de la app) y GET api/PedidosVenta?cliente= (ficha
    /// de cliente) sin N+1 y partiendo de las líneas pendientes. Con un contexto falso en memoria se
    /// fija QUÉ se devuelve (solo pedidos con líneas pendientes, sumas sin duplicar por los grupos
    /// de vendedor, tieneFechasFuturas y ultimoSeguimiento correctos) y CUÁNTAS veces se toca cada
    /// tabla (una, no una por pedido).
    /// </summary>
    [TestClass]
    public class PedidosVentaListadoTests
    {
        private NVEntities db;
        private IServicioVendedores servicioVendedores;
        private PedidosVentaController controller;

        private readonly List<CabPedidoVta> cabeceras = new List<CabPedidoVta>();
        private readonly List<LinPedidoVta> lineas = new List<LinPedidoVta>();
        private readonly List<VendedorPedidoGrupoProducto> gruposVendedor = new List<VendedorPedidoGrupoProducto>();
        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioVendedores = A.Fake<IServicioVendedores>();
            A.CallTo(() => db.CabPedidoVtas).Returns(FakeDbSet(cabeceras));
            A.CallTo(() => db.LinPedidoVtas).Returns(FakeDbSet(lineas));
            A.CallTo(() => db.VendedoresPedidosGruposProductos).Returns(FakeDbSet(gruposVendedor));
            A.CallTo(() => db.EnviosAgencias).Returns(FakeDbSet(envios));
            controller = new PedidosVentaController(db, servicioVendedores);
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

        private static readonly DateTime FUTURO = new DateTime(2030, 1, 1);
        private static readonly DateTime PASADO = new DateTime(2000, 1, 1);

        private void Pedido(int numero, string cliente, string vendedor, params (short estado, decimal importe, DateTime entrega)[] lineasPedido)
        {
            cabeceras.Add(new CabPedidoVta
            {
                Empresa = "1  ",
                Número = numero,
                Nº_Cliente = cliente,
                Vendedor = vendedor,
                Ruta = "16 ",
                Cliente = new Cliente { Nombre = "CLIENTE " + cliente, Dirección = "C/ Real 1", CodPostal = "28110", Población = "ALGETE", Provincia = "MADRID" }
            });
            foreach ((short estado, decimal importe, DateTime entrega) l in lineasPedido)
            {
                lineas.Add(new LinPedidoVta
                {
                    Empresa = "1  ",
                    Número = numero,
                    Estado = l.estado,
                    TipoLinea = 1,
                    Picking = 0,
                    Fecha_Entrega = l.entrega,
                    Base_Imponible = l.importe,
                    Total = l.importe * 1.21M
                });
            }
        }

        [TestMethod]
        public async Task GetPedidosVenta_SinVendedor_SoloLosPedidosConLineasPendientesYSusImportes()
        {
            Pedido(925100, "15191     ", "NV ", ((short)-1, 100M, PASADO));                       // pendiente
            Pedido(925101, "15191     ", "NV ", ((short)4, 50M, PASADO));                         // ya facturado: fuera
            Pedido(925102, "29268     ", "ASH", ((short)1, 10M, PASADO), ((short)4, 999M, PASADO)); // en curso + facturada: solo cuenta la pendiente

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("");

            CollectionAssert.AreEqual(new[] { 925102, 925100 }, lista.Select(p => p.numero).ToArray(), "solo los que tienen líneas pendientes, del más nuevo al más viejo");
            ResumenPedidoVentaDTO mixto = lista.Single(p => p.numero == 925102);
            Assert.AreEqual(10M, mixto.baseImponible, "la línea facturada no suma");
            Assert.AreEqual("29268", mixto.cliente, "los char llegan recortados");
            Assert.AreEqual("CLIENTE 29268", mixto.nombre);
            Assert.AreEqual("16", mixto.ruta);
            Assert.IsFalse(mixto.tienePendientes, "en curso (1) no es pendiente (<0)");
            Assert.IsTrue(lista.Single(p => p.numero == 925100).tienePendientes);
        }

        [TestMethod]
        public async Task GetPedidosVenta_DeUnCliente_UnPedidoFacturadoNoSaleConPicking()
        {
            // NestoAPI#11 (2016): el listado de pedidos de un cliente incluye los facturados, y sus líneas
            // conservan el nº de picking: salían «con picking» cuando ya habían salido hace tiempo.
            Pedido(925200, "15191     ", "NV ", ((short)4, 50M, PASADO));
            lineas.Last().Picking = 1234;
            Pedido(925201, "15191     ", "NV ", ((short)1, 20M, PASADO));
            lineas.Last().Picking = 1235;

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("", "15191     ");

            Assert.IsFalse(lista.Single(p => p.numero == 925200).tienePicking, "facturado: ya no tiene picking pendiente");
            Assert.IsTrue(lista.Single(p => p.numero == 925201).tienePicking, "en curso con picking");
        }

        [TestMethod]
        public async Task GetPedidosVenta_TieneFechasFuturas_SoloSiUnaLineaPendienteEntregaMasTarde()
        {
            Pedido(925100, "15191", "NV ", ((short)-1, 100M, FUTURO));
            Pedido(925101, "15191", "NV ", ((short)-1, 100M, PASADO));
            Pedido(925102, "15191", "NV ", ((short)-1, 100M, PASADO), ((short)4, 1M, FUTURO)); // la futura está facturada: no cuenta

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("");

            Assert.IsTrue(lista.Single(p => p.numero == 925100).tieneFechasFuturas);
            Assert.IsFalse(lista.Single(p => p.numero == 925101).tieneFechasFuturas);
            Assert.IsFalse(lista.Single(p => p.numero == 925102).tieneFechasFuturas);
        }

        [TestMethod]
        public async Task GetPedidosVenta_UltimoSeguimiento_EsElDelEnvioMasRecienteYNuloSinEnvios()
        {
            Pedido(925100, "15191", "NV ", ((short)1, 100M, PASADO));
            Pedido(925101, "15191", "NV ", ((short)1, 100M, PASADO));
            envios.Add(new EnviosAgencia { Numero = 1, Pedido = 925100, CodigoBarras = "ANTIGUO" });
            envios.Add(new EnviosAgencia { Numero = 2, Pedido = 925100, CodigoBarras = "RECIENTE" });
            envios.Add(new EnviosAgencia { Numero = 3, Pedido = null, CodigoBarras = "SIN PEDIDO" });

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("");

            Assert.AreEqual("RECIENTE", lista.Single(p => p.numero == 925100).ultimoSeguimiento);
            Assert.IsNull(lista.Single(p => p.numero == 925101).ultimoSeguimiento);
        }

        [TestMethod]
        public async Task GetPedidosVenta_NoConsultaPorPedido_CadaTablaSeTocaUnaVez()
        {
            // El fallo de la issue: 290 pedidos = 580 consultas más. Con el contexto falso se cuenta
            // cuántas veces se pide cada DbSet: una por listado, se devuelvan los pedidos que se devuelvan.
            for (int i = 0; i < 25; i++)
            {
                Pedido(925100 + i, "15191", "NV ", ((short)-1, 10M, PASADO));
                envios.Add(new EnviosAgencia { Numero = i + 1, Pedido = 925100 + i, CodigoBarras = "CB" + i });
            }

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("");

            Assert.AreEqual(25, lista.Count);
            A.CallTo(() => db.LinPedidoVtas).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.EnviosAgencias).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPedidosVenta_ConVendedor_IncluyeLosDelEquipoYLosDeGruposDeProducto_SinDuplicarSumas()
        {
            A.CallTo(() => servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, "ASH"))
                .Returns(Task.FromResult(new List<VendedorDTO> { new VendedorDTO { vendedor = "ASH" }, new VendedorDTO { vendedor = "VEN1" } }));
            Pedido(925100, "15191", "ASH", ((short)-1, 100M, PASADO));   // suyo
            Pedido(925101, "15191", "NV ", ((short)-1, 100M, PASADO));   // de otro, pero con DOS grupos de producto de su equipo
            gruposVendedor.Add(new VendedorPedidoGrupoProducto { Empresa = "1  ", Pedido = 925101, GrupoProducto = "COS", Vendedor = "VEN1" });
            gruposVendedor.Add(new VendedorPedidoGrupoProducto { Empresa = "1  ", Pedido = 925101, GrupoProducto = "PEL", Vendedor = "ASH" });
            Pedido(925102, "15191", "OTR", ((short)-1, 100M, PASADO));   // de otro equipo: fuera

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("ASH");

            CollectionAssert.AreEqual(new[] { 925101, 925100 }, lista.Select(p => p.numero).ToArray());
            Assert.AreEqual(100M, lista.Single(p => p.numero == 925101).baseImponible, "el left join de antes duplicaba la línea por cada grupo (200)");
        }

        [TestMethod]
        public async Task GetPedidosVenta_ConCliente_DevuelveTambienLosFacturadosPeroLasFechasFuturasSoloDePendientes()
        {
            Pedido(925100, "15191", "NV ", ((short)4, 100M, FUTURO));                             // facturado: en la ficha se ve
            Pedido(925101, "15191", "NV ", ((short)-1, 100M, FUTURO));
            Pedido(925102, "29268", "NV ", ((short)-1, 100M, FUTURO));                             // otro cliente

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("", "15191");

            CollectionAssert.AreEqual(new[] { 925101, 925100 }, lista.Select(p => p.numero).ToArray());
            Assert.IsFalse(lista.Single(p => p.numero == 925100).tieneFechasFuturas, "sin líneas pendientes no hay fechas futuras que valgan");
            Assert.IsTrue(lista.Single(p => p.numero == 925101).tieneFechasFuturas);
            A.CallTo(() => db.LinPedidoVtas).MustHaveHappenedOnceExactly();
        }
    }
}
