using NestoAPI.Models;
using NestoAPI.Models.Alquileres;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Alquileres
{
    /// <summary>
    /// Lee la lista de productos en alquiler ejecutando el SP prdProductosAlquilerLista
    /// (Nesto#340, Fase 1C.1: se mueve la lectura del cliente Nesto al API sin tocar el SP).
    /// </summary>
    public class ProductosAlquilerService : IProductosAlquilerService
    {
        private readonly NVEntities db;

        public ProductosAlquilerService()
        {
            db = new NVEntities();
        }

        internal ProductosAlquilerService(NVEntities db)
        {
            this.db = db;
        }

        public async Task<List<ProductoAlquilerDTO>> LeerProductosAlquilerAsync()
        {
            // El SP no tiene parámetros y devuelve las columnas Empresa, Número, Nombre, Stock,
            // StockAlquileres y Diferencia. SqlQuery<T> empareja por nombre de columna, así que se
            // lee en un tipo crudo con la columna "Número" (válida como identificador C#) y luego se
            // proyecta al DTO de nombres limpios.
            List<FilaSp> filas = await db.Database
                .SqlQuery<FilaSp>("EXEC prdProductosAlquilerLista")
                .ToListAsync()
                .ConfigureAwait(false);

            return filas.Select(f => new ProductoAlquilerDTO
            {
                Empresa = f.Empresa?.Trim(),
                Numero = f.Número?.Trim(),
                Nombre = f.Nombre?.Trim(),
                Stock = f.Stock,
                StockAlquileres = f.StockAlquileres,
                Diferencia = f.Diferencia
            }).ToList();
        }

        // Nesto#340 Fase 1C.2: pestaña "Movimientos" de un alquiler (líneas del pedido de venta).
        // Sustituye la lectura EF DbContext.LinPedidoVta del cliente Nesto.
        public async Task<List<MovimientoAlquilerDTO>> LeerMovimientosAlquilerAsync(string empresa, int pedido)
        {
            // Se materializa primero y se proyecta en memoria para poder usar ?.Trim() (no
            // traducible por EF) y dejar nombres/columnas limpias en el DTO.
            List<LinPedidoVta> lineas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == pedido)
                .OrderBy(l => l.Nº_Orden)
                .ToListAsync()
                .ConfigureAwait(false);

            return lineas.Select(l => new MovimientoAlquilerDTO
            {
                NumeroOrden = l.Nº_Orden,
                FechaEntrega = l.Fecha_Entrega,
                Producto = l.Producto?.Trim(),
                Texto = l.Texto?.Trim(),
                Cantidad = l.Cantidad,
                Precio = l.Precio,
                Total = l.Total,
                Estado = l.Estado
            }).ToList();
        }

        // Tipo crudo intermedio: sus nombres de propiedad coinciden con las columnas del SP.
        private class FilaSp
        {
            public string Empresa { get; set; }
            public string Número { get; set; }
            public string Nombre { get; set; }
            public int Stock { get; set; }
            public int StockAlquileres { get; set; }
            public int Diferencia { get; set; }
        }
    }
}
