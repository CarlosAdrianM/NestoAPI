using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#517 (23/09/26): POST api/PedidosVenta/OfertasSugeridas tumbó RDS2016. Cada candidata
    /// validaba el pedido ENTERO contra la BD y los validadores releían los mismos productos una y otra
    /// vez (ProductosMismoPrecio recorre el pedido por cada producto). Estos tests fijan el coste: con la
    /// caché por petición, las lecturas crecen con el nº de productos distintos, NO con productos × candidatas.
    ///
    /// Cifras medidas con el pedido de abajo (10 productos, 30 líneas, 10 candidatas N+M):
    ///   - sin caché en las validaciones (como antes): 3.651 lecturas al servicio (= consultas a la BD);
    ///   - con la caché de la petición: 41 (unas 4 por producto distinto).
    /// En producción cada lectura era una consulta: con varios pedidos a la vez, miles por segundo.
    /// </summary>
    [TestClass]
    public class CosteSugerenciasOfertasTests
    {
        private const int PRODUCTOS = 10;
        private const int LINEAS_POR_PRODUCTO = 3;

        private static IServicioPrecios ServicioContador()
        {
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            for (int i = 1; i <= PRODUCTOS; i++)
            {
                string numero = "P" + i.ToString("00");
                A.CallTo(() => servicio.BuscarProducto(numero)).Returns(new Producto
                {
                    Número = numero, Nombre = "PRODUCTO " + numero, Familia = "Familia" + i, Grupo = "COS", SubGrupo = "001", PVP = 10 + i, Aplicar_Dto = true, Estado = 0
                });
                A.CallTo(() => servicio.BuscarOfertasPermitidas(numero)).Returns(new List<OfertaPermitida>
                {
                    new OfertaPermitida { NºOrden = i, Número = numero, CantidadConPrecio = 6, CantidadRegalo = 1 }
                });
            }
            return servicio;
        }

        private static PedidoVentaDTO PedidoDe30Lineas()
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "10458", contacto = "0", Lineas = new List<LineaPedidoVentaDTO>() }; // no El Edén (15191), que se salta la validación
            int id = 1;
            for (int i = 1; i <= PRODUCTOS; i++)
            {
                for (int j = 0; j < LINEAS_POR_PRODUCTO; j++)
                {
                    // 3 líneas de 2, 2 y 3 unidades = 7 cobradas: todas las candidatas cumplen un 6+1 sin aplicar.
                    pedido.Lineas.Add(new LineaPedidoVentaDTO
                    {
                        id = id++,
                        Producto = "P" + i.ToString("00"),
                        Cantidad = j == 2 ? 3 : 2,
                        PrecioUnitario = 10 + i,
                        tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                        almacen = "ALG"
                    });
                }
            }
            return pedido;
        }

        private static int Lecturas(IServicioPrecios servicio) => Fake.GetCalls(servicio).Count();

        [TestMethod]
        public void ConLaCacheDeLaPeticion_LasLecturasNoSeMultiplicanPorLasCandidatas()
        {
            IServicioPrecios servicio = ServicioContador();

            List<SugerenciaOfertaDTO> sugerencias = GestorSugerenciasOfertas.Calcular(PedidoDe30Lineas(), servicio);

            Assert.AreEqual(PRODUCTOS, sugerencias.Count(s => s.Tipo == GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA),
                "Cada producto tiene su 6+1 sin aplicar: el cálculo sigue siendo el mismo");
            // Tipos de lectura por producto que usan el cálculo y los validadores: producto, ofertas permitidas,
            // descuentos, combinadas, escalonadas, regalo por importe, ganavisiones, stock, incompatibilidades
            // de familia y última compra de familia (10), más los regalos vigentes (1 por petición) y las
            // llamadas que dependen del pedido (FiltrarLineas, CalcularImporteGrupo), que no se cachean.
            int cota = PRODUCTOS * 10 + 1 + 20;
            int lecturas = Lecturas(servicio);
            Assert.IsTrue(lecturas <= cota, $"Lecturas al servicio con caché: {lecturas} (cota {cota})");
        }

        [TestMethod]
        public void ComparadoConLoDeAntes_LaCacheAhorraAlMenosUnOrdenDeMagnitud()
        {
            IServicioPrecios sinCache = ServicioContador();
            IServicioPrecios conCache = ServicioContador();
            PedidoVentaDTO pedido = PedidoDe30Lineas();

            // Así funcionaba antes: cada validación iba directa al servicio (a la BD).
            _ = GestorSugerenciasOfertas.Calcular(pedido, sinCache, p => GestorPrecios.EsPedidoValido(p, sinCache));
            _ = GestorSugerenciasOfertas.Calcular(pedido, conCache);

            int antes = Lecturas(sinCache);
            int ahora = Lecturas(conCache);
            Console.WriteLine($"NestoAPI#517 lecturas al servicio: antes {antes}, ahora {ahora}");
            Assert.IsTrue(ahora * 10 <= antes, $"Antes {antes} lecturas, ahora {ahora}");
        }

        [TestMethod]
        public void EsPedidoValido_ConElServicioDeLaPeticion_ReutilizaLasLecturas()
        {
            IServicioPrecios servicio = ServicioContador();
            IServicioPrecios peticion = new ServicioPreciosCacheado(servicio);
            PedidoVentaDTO pedido = PedidoDe30Lineas();

            _ = GestorPrecios.EsPedidoValido(pedido, peticion);
            int primera = Lecturas(servicio);
            _ = GestorPrecios.EsPedidoValido(pedido, peticion);

            Assert.AreEqual(primera, Lecturas(servicio) - LlamadasQueNoSeCachean(servicio, primera),
                "La segunda validación con la misma caché solo repite lo que depende del pedido");
        }

        private static int LlamadasQueNoSeCachean(IServicioPrecios servicio, int desde)
            => Fake.GetCalls(servicio).Skip(desde)
                .Count(c => c.Method.Name == nameof(IServicioPrecios.FiltrarLineas) || c.Method.Name == nameof(IServicioPrecios.CalcularImporteGrupo));
    }
}
