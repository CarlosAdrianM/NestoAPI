using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Infrastructure.Stocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606 (packs montables, 18/11/26): un pack (titular de un kit) que nunca se compra y se monta en Algete con sus componentes.
    /// Caso real: 44376 PACK REPOSICION LIFTING (40007 + 40010 + 40056, una de cada; 110, 11 y 184 en Algete) en un pedido «todo junto»
    /// con 43521 en Alcobendas: decía «sin fecha» y dejaba el pedido entero sin fecha.
    /// </summary>
    [TestClass]
    public class MontajeKitsFechaEntregaTests
    {
        // Miércoles 18/11/2026 a las 10:30 (antes del corte de las 11:00)
        private static readonly DateTime AHORA = new DateTime(2026, 11, 18, 10, 30, 0);
        private static readonly DateTime HOY = AHORA.Date;
        private static readonly DateTime VIERNES = new DateTime(2026, 11, 20);
        private const string PACK = "44376";

        private static bool EsFestivo(DateTime dia, string almacen) => dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday;

        /// <summary>Alcobendas → Algete los jueves, llega a las 13:30 (después del corte: el pedido sale el viernes).</summary>
        private static List<ReposicionCalendario> Calendario() => new List<ReposicionCalendario>
        {
            new ReposicionCalendario
            {
                Empresa = "1", AlmacenOrigen = "ALC", AlmacenDestino = "ALG", DiaSemana = 4,
                HoraCierre = new TimeSpan(9, 0, 0), HoraLlegadaHabitual = new TimeSpan(13, 30, 0), Activo = true
            }
        };

        private static void Stock(DatosSombraModoServicio datos, string producto, string almacen, int stock, int pendientes = 0)
        {
            ResumenStocksProductos.Sumar(datos.Resumen.StockAlmacen, ResumenStocksProductos.Clave(producto, almacen), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.StockSedes, ResumenStocksProductos.Clave(producto), stock);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarAlmacen, ResumenStocksProductos.Clave(producto, almacen), pendientes);
            ResumenStocksProductos.Sumar(datos.Resumen.PendienteEntregarTotal, ResumenStocksProductos.Clave(producto), pendientes);
        }

        private static void Pack(DatosSombraModoServicio datos, string pack, params (string Componente, int Cantidad)[] componentes)
        {
            foreach (var c in componentes)
            {
                datos.KitsMontaje.Add(new ComponenteKitFechaEntregaAgencia { Kit = pack, Componente = c.Componente, Cantidad = c.Cantidad });
            }
        }

        /// <summary>El caso real: el pack sin stock propio y sus componentes en Algete (110, 11, 184).</summary>
        private static DatosSombraModoServicio DatosCasoReal(int stock40010 = 11, int pendientes40010 = 0)
        {
            var datos = new DatosSombraModoServicio();
            Pack(datos, PACK, ("40007", 1), ("40010", 1), ("40056", 1));
            Stock(datos, "40007", "ALG", 110);
            Stock(datos, "40010", "ALG", stock40010, pendientes40010);
            Stock(datos, "40056", "ALG", 184);
            datos.Nombres[ResumenStocksProductos.Clave("40010")] = "ESPUMA LIMPIADORA";
            Stock(datos, "43521", "ALC", 3);
            Stock(datos, "A", "ALG", 10);
            Stock(datos, "B", "ALG", 10);
            return datos;
        }

        private static LineaPedidoFechaEntregaAgencia Linea(string producto, int cantidad = 1) =>
            new LineaPedidoFechaEntregaAgencia { Producto = producto, Almacen = "ALG", Cantidad = cantidad, BaseImponible = 10 };

        private static ResultadoFechaEntregaAgencia Calcular(DatosSombraModoServicio datos, byte modo, params LineaPedidoFechaEntregaAgencia[] pedido)
        {
            List<LineaFechaEntregaAgencia> lineas = RepartidorStockFechaEntregaAgencia.Repartir(pedido, datos, new[] { "ALC", "REI" }, HOY);
            return new CalculadoraFechaEntregaAgencia(EsFestivo).Calcular(new EntradaFechaEntregaAgencia
            {
                Lineas = lineas,
                ModoServicio = modo,
                ModoFacturacion = Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS,
                Ruta = "FW",
                Ahora = AHORA,
                HoraCorte = new TimeSpan(11, 0, 0),
                Calendario = Calendario()
            });
        }

        private static PartidaFechaEntregaAgenciaDTO DelPack(ResultadoFechaEntregaAgencia resultado) => resultado.Desglose.Single(p => p.Producto == PACK);

        // ------------------------------------------------------------------ el reparto

        [TestMethod]
        public void CasoReal_ElPackSeMontaConLosComponentesDeAlgete()
        {
            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea(PACK) }, DatosCasoReal(), new[] { "ALC" }, HOY).Single();

            Assert.AreEqual(0, linea.EnAlgete);
            Assert.AreEqual(1, linea.Montaje);
            Assert.AreEqual(0, linea.MontajeConComponentes);
        }

        [TestMethod]
        public void CasoReal_TodoJunto_SoloEsperaALaReposicionDeAlcobendas()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular(DatosCasoReal(), Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                Linea("A"), Linea("B"), Linea(PACK), Linea("43521"));

            PartidaFechaEntregaAgenciaDTO pack = DelPack(resultado);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, pack.Origen);
            Assert.IsNull(pack.ComponenteQueFalta);
            Assert.AreEqual(HOY, pack.PuedeSalirEl);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_SEGURA, pack.Certeza);
            Assert.AreEqual(VIERNES, resultado.EntregaCompleta, resultado.Motivo);
            StringAssert.Contains(resultado.Motivo, "1 ud. de 44376 se monta en Algete con sus componentes: puede salir el miércoles 18/11.");
            Assert.IsFalse(resultado.Motivo.Contains("sin fecha"), resultado.Motivo);
        }

        [TestMethod]
        public void PackConStockPropio_ComoHoy()
        {
            DatosSombraModoServicio datos = DatosCasoReal();
            Stock(datos, PACK, "ALG", 1);

            ResultadoFechaEntregaAgencia resultado = Calcular(datos, Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea(PACK));

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, DelPack(resultado).Origen);
            Assert.AreEqual(HOY, resultado.EntregaCompleta);
        }

        [TestMethod]
        public void ProductoNormalSinStock_ComoHoy()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular(new DatosSombraModoServicio(), Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea("Z"));

            PartidaFechaEntregaAgenciaDTO z = resultado.Desglose.Single();
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, z.Origen);
            Assert.IsNull(z.ComponenteQueFalta);
            Assert.AreEqual("no hay stock ni fecha de llegada", z.MotivoSinFecha);
            Assert.IsNull(resultado.EntregaCompleta);
        }

        [TestMethod]
        public void ComponenteComprometidoConOtrosPedidos_NoSeMontaYDiceQueFalta()
        {
            // Las 11 de 40010 las deben otros pedidos: no queda ninguna para el pack
            ResultadoFechaEntregaAgencia resultado = Calcular(DatosCasoReal(pendientes40010: 11), Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea(PACK));

            PartidaFechaEntregaAgenciaDTO pack = DelPack(resultado);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, pack.Origen);
            Assert.AreEqual("40010", pack.ComponenteQueFalta);
            StringAssert.Contains(pack.MotivoSinFecha, "para montarlo en Algete falta el componente 40010 ESPUMA LIMPIADORA");
            Assert.IsNull(resultado.EntregaCompleta);
        }

        [TestMethod]
        public void ComponenteQueNecesitanOtrosPacksPendientes_NoSePrometeDosVeces()
        {
            // Otros pedidos deben 11 packs que no hay: se llevan las 11 de 40010
            DatosSombraModoServicio datos = DatosCasoReal();
            Stock(datos, PACK, "ALG", 0, pendientes: 11);

            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea(PACK) }, datos, new[] { "ALC" }, HOY).Single();

            Assert.AreEqual(0, linea.Montaje);
            Assert.AreEqual(1, linea.MontajeConComponentes);
            Assert.AreEqual("40010", linea.ComponentesMontaje.Single().Producto);
        }

        [TestMethod]
        public void ElComponenteSueltoDelPropioPedido_VaAntes()
        {
            LineaFechaEntregaAgencia pack = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea("40010", 11), Linea(PACK) }, DatosCasoReal(), new[] { "ALC" }, HOY)[1];

            Assert.AreEqual(0, pack.Montaje);
            Assert.AreEqual(1, pack.MontajeConComponentes);
        }

        [TestMethod]
        public void DosPacks_SeMontaLoQueDaElComponenteMasEscaso()
        {
            LineaFechaEntregaAgencia linea = RepartidorStockFechaEntregaAgencia.Repartir(new[] { Linea(PACK, 12) }, DatosCasoReal(), new[] { "ALC" }, HOY).Single();

            Assert.AreEqual(11, linea.Montaje);
            Assert.AreEqual(1, linea.MontajeConComponentes);
        }

        [TestMethod]
        public void ComponenteEnTienda_ElPackSaleConLaReposicionYLoDice()
        {
            DatosSombraModoServicio datos = DatosCasoReal(stock40010: 0);
            Stock(datos, "40010", "ALC", 5);

            ResultadoFechaEntregaAgencia resultado = Calcular(datos, Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea(PACK));

            PartidaFechaEntregaAgenciaDTO pack = DelPack(resultado);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, pack.Origen);
            Assert.AreEqual("40010", pack.ComponenteQueFalta);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, pack.OrigenComponente);
            Assert.AreEqual("ALC", pack.Tienda);
            Assert.AreEqual(VIERNES, pack.PuedeSalirEl);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, pack.Certeza);
            Assert.AreEqual(VIERNES, resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "1 ud. de 44376 se monta en Algete cuando llegue el componente 40010 ESPUMA LIMPIADORA, que viene de Alcobendas con la reposición: puede salir el viernes 20/11");
        }

        [TestMethod]
        public void ComponenteSinStock_ElPackSinFechaNombraElComponente()
        {
            ResultadoFechaEntregaAgencia resultado = Calcular(DatosCasoReal(stock40010: 0), Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea("A"), Linea(PACK));

            PartidaFechaEntregaAgenciaDTO pack = DelPack(resultado);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, pack.Origen);
            Assert.AreEqual("40010", pack.ComponenteQueFalta);
            Assert.IsNull(resultado.EntregaCompleta);
            StringAssert.Contains(resultado.Motivo, "1 ud. de 44376 sin fecha: para montarlo en Algete falta el componente 40010 ESPUMA LIMPIADORA: no hay stock ni fecha de llegada.");
        }

        [TestMethod]
        public void ComponenteDelProveedorVencido_SeAvisaConElComponente()
        {
            DatosSombraModoServicio datos = DatosCasoReal(stock40010: 0);
            datos.PendienteRecibir[ResumenStocksProductos.Clave("40010", "ALG")] = 5;
            datos.FechaPrevista[ResumenStocksProductos.Clave("40010", "ALG")] = new DateTime(2026, 11, 16);

            ResultadoFechaEntregaAgencia resultado = Calcular(datos, Constantes.Pedidos.ModosServicio.TODO_JUNTO, Linea(PACK));

            Assert.AreEqual("40010", resultado.ProveedorVencido.Single().Producto);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, DelPack(resultado).OrigenComponente);
        }

        // ------------------------------------------------------------------ el resto de piezas

        [TestMethod]
        public void Promesa_ElPackQueEsperaUnComponente_DiceElComponente()
        {
            var pack = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, Producto = PACK, Unidades = 1, PuedeSalirEl = VIERNES,
                ComponenteQueFalta = "40010", OrigenComponente = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Tienda = "ALC"
            };

            Assert.AreEqual("para montarlo falta el componente 40010, que viene de Alcobendas", TextosPromesaRiesgo.FraseLarga(pack, false, HOY));
            pack.ComponenteQueFalta = null;
            Assert.AreEqual("hay que montarlo en Algete", TextosPromesaRiesgo.FraseLarga(pack, false, HOY));
        }

        [TestMethod]
        public void Fotos_ElPackQueSeMontaYaSaleDelStock_ElQueEsperaSeMideComoSuComponente()
        {
            var ya = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, Producto = PACK, Unidades = 1, PuedeSalirEl = HOY };
            var espera = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, Producto = PACK, Unidades = 1, PuedeSalirEl = VIERNES,
                ComponenteQueFalta = "40010", OrigenComponente = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA
            };

            Assert.IsTrue(ReglasFotosFechaEntrega.SaleDelStock(ya));
            Assert.IsFalse(ReglasFotosFechaEntrega.SaleDelStock(espera));
            Assert.AreEqual(CasosFotoFechaEntrega.INTERNO, CasosFotoFechaEntrega.De(espera));
        }

        [TestMethod]
        public void Orientativas_LaDeUnPackNoSeMideComoSuya()
        {
            var pack = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = PACK, Unidades = 1, FechaOrientativa = VIERNES, ComponenteQueFalta = "40010"
            };

            Assert.IsFalse(ReglasFechasOrientativas.EsOrientativaSinPedir(pack));
            StringAssert.StartsWith(TextosCertezaFechaEntrega.SinFechaOrientativa(pack, d => d.ToString("dd/MM")), "para montarlo en Algete falta el componente 40010");
        }

        [TestMethod]
        public void SqlKitsMontaje_UnaConsultaSinMateriasPrimas()
        {
            string sql = RepositorioSombraModoServicioSql.SQL_KITS_MONTAJE;

            StringAssert.Contains(sql, "pm.Grupo = 'MTP'");
            StringAssert.Contains(sql, "IN ({0})");
        }

        // ------------------------------------------------------------------ de punta a punta: el correo del pedido

        [TestMethod]
        public async Task Correo_CasoReal_ElPackSeMontaYElPedidoTieneFecha()
        {
            ServicioFechaEntregaAgencia.OlvidarAvisos();
            IRepositorioFechaEntregaAgencia repositorio = A.Fake<IRepositorioFechaEntregaAgencia>();
            A.CallTo(() => repositorio.LeerCalendario(A<string>._)).Returns(Task.FromResult(Calendario()));
            A.CallTo(() => repositorio.LeerHoraCorte(A<string>._)).Returns(new TimeSpan(11, 0, 0));
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<int?>._, A<IEnumerable<string>>._)).ReturnsLazily(() => DatosCasoReal());
            A.CallTo(() => repositorio.LeerPedido("1", 930000)).Returns(Task.FromResult(new PedidoFechaEntregaAgencia
            {
                Empresa = "1", Numero = 930000, Ruta = "FW", ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO, ServirJunto = true,
                Lineas = new List<LineaPedidoFechaEntregaAgencia> { Linea("A"), Linea("B"), Linea(PACK), Linea("43521") }
            }));
            var servicio = new ServicioFechaEntregaAgencia(repositorio, () => AHORA, EsFestivo, _ => { },
                repartoStock: RepartoStockFalso.DesdeSombra(repositorio));
            var lineasCorreo = new[]
            {
                new LineaPedidoVentaDTO { Producto = PACK, texto = "PACK REPOSICION LIFTING", tipoLinea = 1 },
                new LineaPedidoVentaDTO { Producto = "43521", texto = "PURITY CREMA SEBORREGULADORA", tipoLinea = 1 }
            };

            FechaEntregaAgenciaDTO fecha = await servicio.CalcularPedido("1", 930000);
            string html = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(fecha, false, false, lineasCorreo, 6);

            Assert.AreEqual(VIERNES, fecha.FechaEntregaAgencia, fecha.Motivo);
            StringAssert.Contains(html, "<li>Se monta en Algete con sus componentes: 44376 PACK REPOSICION LIFTING (1 ud.).</li>");
            StringAssert.Contains(html, "Reposición Alcobendas &rarr; Algete");
            Assert.IsFalse(html.Contains("Sin fecha"), html);
        }
    }
}
