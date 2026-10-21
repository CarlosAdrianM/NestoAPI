using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#671 (corte 2a): lo que calcula de noche el job «sugerencias-productos-pares» para la cuarta fuente y el
    /// modelo, sin BD y en C# puro (sin Microsoft.ML.Recommender), en la misma pasada que los pares.
    /// <para><b>Afinidad</b> (item-kNN implícita por coseno): cada cliente es el conjunto de productos sugeribles que ha
    /// comprado en el periodo. La similitud entre dos productos es clientes comunes ÷ √(clientes de A × clientes de B), con al
    /// menos <see cref="MINIMO_CLIENTES_COMUNES"/> clientes comunes, y cada producto se queda con sus
    /// <see cref="VECINOS_POR_PRODUCTO"/> vecinos más parecidos. La afinidad de un cliente con un producto que NO ha comprado
    /// es la suma de las similitudes de ese producto con los que sí compra; se guardan los
    /// <see cref="MAXIMO_AFINIDADES_POR_CLIENTE"/> primeros. Los clientes con más de <see cref="MAXIMO_PRODUCTOS_CLIENTE"/>
    /// productos (distribuidores, tiendas) no cuentan para las similitudes: meterían ruido y n² parejas.</para>
    /// <para><b>Popularidad</b>: clientes distintos en 12 y en 3 meses y cestas en 12 meses, por producto.</para>
    /// <para>Determinista: los empates se resuelven siempre por el código del producto o del cliente.</para>
    /// <para>NestoAPI#671 (corte 2b): las cuentas viven en el núcleo (<see cref="TablasNocturnas"/>), las mismas que usa el
    /// entrenamiento offline; aquí solo se adaptan los tipos.</para>
    /// </summary>
    public static class CalculadoraAfinidadProductos
    {
        public const int MINIMO_CLIENTES_COMUNES = TablasNocturnas.MINIMO_CLIENTES_COMUNES;
        public const int VECINOS_POR_PRODUCTO = TablasNocturnas.VECINOS_POR_PRODUCTO;
        public const int MAXIMO_AFINIDADES_POR_CLIENTE = TablasNocturnas.MAXIMO_AFINIDADES_POR_CLIENTE;
        public const int MAXIMO_PRODUCTOS_CLIENTE = TablasNocturnas.MAXIMO_PRODUCTOS_CLIENTE;
        public const int MESES_POPULARIDAD = TablasNocturnas.MESES_POPULARIDAD;
        public const int MESES_TENDENCIA = TablasNocturnas.MESES_TENDENCIA;

        public static List<AfinidadCliente> Afinidades(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            Func<string, ModeloSugerenciasProductos.Datos.FichaProducto> buscar = MotorSugerenciasProductos.Buscador(fichas);
            return TablasNocturnas.Afinidades(MotorSugerenciasProductos.ACompras(compras), p => ReglasSugerencias.EsSugerible(buscar(p)))
                .Select(a => new AfinidadCliente { Cliente = a.Cliente, Producto = a.Producto, Afinidad = a.Afinidad, Rango = a.Rango })
                .ToList();
        }

        public static List<PopularidadProducto> Popularidad(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas, DateTime hoy)
        {
            Func<string, ModeloSugerenciasProductos.Datos.FichaProducto> buscar = MotorSugerenciasProductos.Buscador(fichas);
            return TablasNocturnas.Popularidad(MotorSugerenciasProductos.ACompras(compras), p => ReglasSugerencias.EsSugerible(buscar(p)), hoy)
                .Select(p => new PopularidadProducto
                {
                    Producto = p.Producto,
                    Clientes12Meses = p.Clientes12Meses,
                    Clientes3Meses = p.Clientes3Meses,
                    Cestas12Meses = p.Cestas12Meses,
                    Rango = p.Rango
                })
                .ToList();
        }

        /// <summary>
        /// NestoAPI#683: popularidad de cada producto y familia por grupo de estado del cliente (el de hoy), con las cuentas del
        /// núcleo (<see cref="TablasNocturnas.PopularidadEstado"/>, las mismas que el entrenamiento con el estado de cada instantánea).
        /// </summary>
        public static List<PopularidadPorEstado> PopularidadEstado(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas,
            IDictionary<string, short?> estados, DateTime hoy)
        {
            Func<string, ModeloSugerenciasProductos.Datos.FichaProducto> buscar = MotorSugerenciasProductos.Buscador(fichas);
            var porCliente = new Dictionary<string, short?>(estados ?? new Dictionary<string, short?>(), StringComparer.OrdinalIgnoreCase);
            return TablasNocturnas.PopularidadEstado(MotorSugerenciasProductos.ACompras(compras), buscar,
                cliente => ModeloSugerenciasProductos.Features.EstadosCliente.Grupo(porCliente.TryGetValue(cliente.Trim(), out short? e) ? e : null), hoy);
        }
    }
}
