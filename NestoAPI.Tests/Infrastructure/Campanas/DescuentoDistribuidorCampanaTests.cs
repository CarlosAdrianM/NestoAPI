using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Campanas;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class DescuentoDistribuidorCampanaTests
    {
        private const string CAMPANA = "Black Friday 2026";

        [TestCleanup]
        public void Limpiar()
        {
            CampanasAcumulanDistribuidores.Reiniciar();
            ClientesDistribuidores.Reiniciar();
        }

        private static void MarcarCampana(DateTime? desde = null, DateTime? hasta = null) =>
            CampanasAcumulanDistribuidores.ConfigurarPara(new CampanaAcumulaDistribuidores
            {
                Campana = CAMPANA,
                FechaDesde = desde ?? DateTime.Today.AddDays(-1),
                FechaHasta = hasta ?? DateTime.Today.AddDays(5)
            });

        // ---------------------------------------------------------------- LA REGLA

        [TestMethod]
        public void Compuesto_LosEjemplosDeCarlos()
        {
            Assert.AreEqual(0.44M, DescuentoDistribuidorCampana.Compuesto(0.30M, 0.20M));
            Assert.AreEqual(0.625M, DescuentoDistribuidorCampana.Compuesto(0.50M, 0.25M));
        }

        [TestMethod]
        public void Compuesto_SinPropio_QuedaElDeLaCampana()
        {
            Assert.AreEqual(0.20M, DescuentoDistribuidorCampana.Compuesto(0M, 0.20M));
        }

        [TestMethod]
        public void Compuesto_RedondeaACuatroDecimales()
        {
            Assert.AreEqual(0.3333M, DescuentoDistribuidorCampana.Compuesto(0.1111M, 0.25M));
        }

        [TestMethod]
        public void Aplicar_DistribuidorConCampanaMarcada_Compone()
        {
            Assert.AreEqual(0.44M, DescuentoDistribuidorCampana.Aplicar(0.30M, 0.30M, 0.20M, true, true, false));
        }

        [TestMethod]
        public void Aplicar_NoDistribuidor_ComoHoy()
        {
            Assert.AreEqual(0.30M, DescuentoDistribuidorCampana.Aplicar(0.30M, 0.30M, 0.20M, true, false, false));
        }

        [TestMethod]
        public void Aplicar_CampanaNoMarcada_ComoHoy()
        {
            Assert.AreEqual(0.30M, DescuentoDistribuidorCampana.Aplicar(0.30M, 0.30M, 0.20M, false, true, false));
        }

        [TestMethod]
        public void Aplicar_ConPrecioEspecialPropio_NoSeCompone()
        {
            Assert.AreEqual(0.30M, DescuentoDistribuidorCampana.Aplicar(0.30M, 0.30M, 0.20M, true, true, true));
        }

        [TestMethod]
        public void Aplicar_NuncaPeorQueHoy()
        {
            // Hoy le sale un 70 % (descuento propio del producto del cliente que el motor da al final); el compuesto no lo baja.
            Assert.AreEqual(0.70M, DescuentoDistribuidorCampana.Aplicar(0.70M, 0.10M, 0.20M, true, true, false));
        }

        [TestMethod]
        public void EstaMarcada_SoloDentroDeSusFechas()
        {
            var marcas = new[] { new CampanaAcumulaDistribuidores { Campana = CAMPANA, FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) } };

            Assert.IsTrue(DescuentoDistribuidorCampana.EstaMarcada(marcas, "black friday 2026 ", new DateTime(2026, 11, 30)));
            Assert.IsFalse(DescuentoDistribuidorCampana.EstaMarcada(marcas, CAMPANA, new DateTime(2026, 12, 1)));
            Assert.IsFalse(DescuentoDistribuidorCampana.EstaMarcada(marcas, CAMPANA, new DateTime(2026, 11, 24)));
            Assert.IsFalse(DescuentoDistribuidorCampana.EstaMarcada(marcas, "Otra", new DateTime(2026, 11, 26)));
        }

        [TestMethod]
        public void EsEstadoDistribuidor_SeisYSesentaYSiete()
        {
            Assert.IsTrue(ClientesDistribuidores.EsEstadoDistribuidor(6));
            Assert.IsTrue(ClientesDistribuidores.EsEstadoDistribuidor(67));
            Assert.IsFalse(ClientesDistribuidores.EsEstadoDistribuidor(0));
            Assert.IsFalse(ClientesDistribuidores.EsEstadoDistribuidor(null));
            Assert.IsTrue(ClientesDistribuidores.EsDistribuidor(new short?[] { 0, 67 }));
        }

        [TestMethod]
        public void ClientesDistribuidores_SinLector_NadieLoEs()
        {
            Assert.IsFalse(ClientesDistribuidores.EsDistribuidor("1", "15000"));
        }

        [TestMethod]
        public void ClientesDistribuidores_SiFallaLaLectura_NoLoEsYSeApunta()
        {
            var errores = new List<Exception>();
            ClientesDistribuidores.Configurar((e, c) => throw new InvalidOperationException("caída"), errores.Add);

            Assert.IsFalse(ClientesDistribuidores.EsDistribuidor("1", "15000"));
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void MaximoAutorizado_PropioMasCampana()
        {
            var filas = new List<DescuentosProducto>
            {
                new DescuentosProducto { Nº_Cliente = "15000", Familia = "Lisap", Descuento = 0.30M },
                new DescuentosProducto { Nº_Producto = "44166", Descuento = 0.20M, Campana = CAMPANA }
            };

            Assert.AreEqual(0.44M, DescuentoDistribuidorCampana.MaximoAutorizado(filas, "44166", c => c == CAMPANA));
            Assert.IsNull(DescuentoDistribuidorCampana.MaximoAutorizado(filas, "44166", c => false));
        }

        // ---------------------------------------------------------------- el motor de punta a punta

        private static Producto ProductoLisap() => new Producto
        {
            Empresa = "1", Número = "44166", Nombre = "GEL FRIO", PVP = 10M, Familia = "Lisap", Grupo = "PEL", SubGrupo = "ACB", Aplicar_Dto = true
        };

        private static NVEntities DbCon(params DescuentosProducto[] filas)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<DescuentosProducto> fake = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            IQueryable<DescuentosProducto> datos = filas.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<DescuentosProducto>)fake).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<DescuentosProducto>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fake).Provider).Returns(new TestDbAsyncQueryProvider<DescuentosProducto>(datos.Provider));
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fake).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fake).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.DescuentosProductoes).Returns(fake);
            return db;
        }

        private static DescuentosProducto FamiliaDelCliente(decimal descuento) =>
            new DescuentosProducto { Empresa = "1", Nº_Cliente = "15000", Familia = "Lisap", CantidadMínima = 0, Descuento = descuento, Producto = new Producto { Número = "OTRO" } };

        private static DescuentosProducto FilaDeCampana(Producto producto, decimal descuento) =>
            new DescuentosProducto { Empresa = "1", Nº_Producto = producto.Número, Producto = producto, CantidadMínima = 1, Descuento = descuento, Campana = CAMPANA, AudienciaOferta = 2 };

        private static PrecioDescuentoProducto Calcular(NVEntities db, Producto producto)
        {
            var datos = new PrecioDescuentoProducto { producto = producto, cliente = "15000", contacto = "0", cantidad = 1, aplicarDescuento = true };
            GestorPrecios.calcularDescuentoProducto(datos, db);
            return datos;
        }

        [TestMethod]
        public void Motor_DistribuidorConCampanaMarcada_DescuentoCompuesto()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();

            PrecioDescuentoProducto r = Calcular(DbCon(FamiliaDelCliente(0.30M), FilaDeCampana(producto, 0.20M)), producto);

            Assert.AreEqual(0.44M, r.descuentoCalculado);
            Assert.AreEqual(10M, r.precioCalculado);
        }

        [TestMethod]
        public void Motor_CampanaNoMarcada_ComoHoyElMejorDeLosDos()
        {
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();

            PrecioDescuentoProducto r = Calcular(DbCon(FamiliaDelCliente(0.30M), FilaDeCampana(producto, 0.20M)), producto);

            Assert.AreEqual(0.30M, r.descuentoCalculado);
        }

        [TestMethod]
        public void Motor_NoDistribuidor_ComoHoy()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("99999");
            Producto producto = ProductoLisap();

            PrecioDescuentoProducto r = Calcular(DbCon(FamiliaDelCliente(0.30M), FilaDeCampana(producto, 0.40M)), producto);

            Assert.AreEqual(0.40M, r.descuentoCalculado, "El mejor de los dos");
        }

        [TestMethod]
        public void Motor_MarcaFueraDeFechas_ComoHoy()
        {
            MarcarCampana(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1));
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();

            PrecioDescuentoProducto r = Calcular(DbCon(FamiliaDelCliente(0.30M), FilaDeCampana(producto, 0.20M)), producto);

            Assert.AreEqual(0.30M, r.descuentoCalculado);
        }

        [TestMethod]
        public void Motor_DistribuidorSinDescuentoPropio_QuedaElDeLaCampana()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();

            PrecioDescuentoProducto r = Calcular(DbCon(FilaDeCampana(producto, 0.20M)), producto);

            Assert.AreEqual(0.20M, r.descuentoCalculado);
        }

        [TestMethod]
        public void Motor_DistribuidorConPrecioEspecialPropio_NoSeCompone()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();
            var precioEspecial = new DescuentosProducto { Empresa = "1", Nº_Cliente = "15000", Contacto = "0", Nº_Producto = producto.Número, Producto = producto, CantidadMínima = 1, Precio = 9M, Descuento = 0M };

            PrecioDescuentoProducto r = Calcular(DbCon(precioEspecial, FamiliaDelCliente(0.30M), FilaDeCampana(producto, 0.20M)), producto);

            // Hoy: el descuento propio del producto del cliente (0 %) pisa todo y manda el precio especial de 9 €.
            Assert.AreEqual(9M, r.precioCalculado);
            Assert.AreEqual(0M, r.descuentoCalculado);
        }

        // ---------------------------------------------------------------- la validación del pedido

        private static IServicioPrecios ServicioCon(Producto producto, params DescuentosProducto[] filas)
        {
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto(producto.Número)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(A<string>._)).Returns(new List<OfertaPermitida>());
            A.CallTo(() => servicio.BuscarDescuentosPermitidos(producto.Número, "15000", "0")).Returns(filas.ToList());
            return servicio;
        }

        private static PedidoVentaDTO PedidoCon(decimal descuento)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15000", contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "44166",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoProducto = descuento
            });
            return pedido;
        }

        [TestMethod]
        public void Validacion_DistribuidorConElCompuesto_EsValida()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();
            IServicioPrecios servicio = ServicioCon(producto,
                new DescuentosProducto { Nº_Cliente = "15000", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30M },
                new DescuentosProducto { Nº_Producto = "44166", CantidadMínima = 1, Descuento = 0.20M, Campana = CAMPANA });

            RespuestaValidacion r = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, PedidoCon(0.44M), servicio);

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        [TestMethod]
        public void Validacion_DistribuidorPorEncimaDelCompuesto_NoEsValida()
        {
            MarcarCampana();
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();
            IServicioPrecios servicio = ServicioCon(producto,
                new DescuentosProducto { Nº_Cliente = "15000", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30M },
                new DescuentosProducto { Nº_Producto = "44166", CantidadMínima = 1, Descuento = 0.20M, Campana = CAMPANA });

            RespuestaValidacion r = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, PedidoCon(0.45M), servicio);

            Assert.IsFalse(r.ValidacionSuperada);
        }

        [TestMethod]
        public void Validacion_NoDistribuidorConElCompuesto_ComoHoyNoEsValida()
        {
            MarcarCampana();
            Producto producto = ProductoLisap();
            IServicioPrecios servicio = ServicioCon(producto,
                new DescuentosProducto { Nº_Cliente = "15000", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30M },
                new DescuentosProducto { Nº_Producto = "44166", CantidadMínima = 1, Descuento = 0.20M, Campana = CAMPANA });

            RespuestaValidacion r = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, PedidoCon(0.44M), servicio);

            Assert.IsFalse(r.ValidacionSuperada);
        }

        [TestMethod]
        public void Validacion_CampanaSinMarca_ComoHoyNoEsValida()
        {
            ClientesDistribuidores.ConfigurarPara("15000");
            Producto producto = ProductoLisap();
            IServicioPrecios servicio = ServicioCon(producto,
                new DescuentosProducto { Nº_Cliente = "15000", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30M },
                new DescuentosProducto { Nº_Producto = "44166", CantidadMínima = 1, Descuento = 0.20M, Campana = CAMPANA });

            RespuestaValidacion r = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, PedidoCon(0.44M), servicio);

            Assert.IsFalse(r.ValidacionSuperada);
        }
    }
}
