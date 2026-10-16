using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#661: las líneas de «Volver a pedir» que devuelve <c>GET api/Pedidos/Cliente</c>, leídas como lo hace
    /// la API (con la confirmación de los regalos de Ganavisiones contra su tabla).
    /// </summary>
    [TestClass]
    public class ServicioPedidosClienteTests
    {
        [TestMethod]
        public async Task LeerPedidosRecientes_Lineas_SinRegaloGanavisionesNiCuponNiPortes_YLaMuestraACeroSiVa()
        {
            string cupon = NestoAPI.Infraestructure.Cupones.ReglasCupon.ProductoCupon;
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 925900,
                Nº_Cliente = "15816",
                Fecha = DateTime.Today.AddDays(-3),
                Forma_Pago = "RCB",
                PlazosPago = "CONTADO",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    Linea(1, "LACA500    ", "LACA 500 ML", 2, 10m),
                    Linea(2, "REGALO     ", "CEPILLO REGALO (BONIFICADO)", 1, 0m),
                    Linea(3, "MUESTRA    ", "MUESTRA CREMA", 1, 0m),
                    Linea(4, cupon, "Cupón 10 €", -1, -8.26m),
                    Linea(5, "62400000", "Portes", 1, 4m, Constantes.TiposLineaVenta.CUENTA_CONTABLE),
                    Linea(6, "LACA500    ", "LACA 500 ML", 1, 0m, oferta: 33)
                }
            };

            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabPedidoVtas).Returns(Fake(new List<CabPedidoVta> { pedido }));
            A.CallTo(() => db.Prepagos).Returns(Fake(new List<Prepago>()));
            A.CallTo(() => db.EnviosAgencias).Returns(Fake(new List<EnviosAgencia>()));
            A.CallTo(() => db.Ganavisiones).Returns(Fake(new List<Ganavision>
            {
                new Ganavision { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, ProductoId = "REGALO" }
            }));

            List<PedidoClienteResumenDTO> resultado = await new ServicioPedidosCliente(db)
                .LeerPedidosRecientes(Constantes.Empresas.EMPRESA_POR_DEFECTO, "15816", 60);

            List<LineaPedidoClienteResumenDTO> lineas = resultado.Single().Lineas;
            CollectionAssert.AreEqual(new[] { "LACA500", "MUESTRA" }, lineas.Select(l => l.Producto).ToArray(),
                "El regalo de Ganavisiones (confirmado en su tabla), el cupón y los portes no se vuelven a pedir; la muestra a 0 sí");
            Assert.AreEqual(3, lineas[0].Cantidad, "La oferta 2+1 son dos líneas del mismo producto: van en una");
            Assert.AreEqual("LACA 500 ML", lineas[0].Nombre);
            Assert.AreEqual(1, lineas[1].Cantidad);
        }

        private static LinPedidoVta Linea(int orden, string producto, string texto, short cantidad, decimal baseImponible,
            int tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, int? oferta = null)
            => new LinPedidoVta
            {
                Empresa = "1",
                Número = 925900,
                Nº_Orden = orden,
                Nº_Cliente = "15816",
                TipoLinea = (byte)tipoLinea,
                Producto = producto,
                Texto = texto,
                Cantidad = cantidad,
                Base_Imponible = baseImponible,
                Total = baseImponible * 1.21m,
                NºOferta = oferta,
                Estado = Constantes.EstadosLineaVenta.PENDIENTE
            };

        internal static DbSet<T> Fake<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            IQueryable<T> data = datos.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            return fake;
        }
    }
}
