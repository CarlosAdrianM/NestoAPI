using NestoAPI.Models;
using NestoAPI.Models.Alquileres;
using System;
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

        // Nesto#340 Fase 1C.2: pestaña "Compra" de un alquiler (líneas del pedido de compra del
        // aparato concreto). Sustituye la lectura EF DbContext.LinPedidoCmp del cliente Nesto, que
        // filtraba por Producto + NumSerie ordenando por NºOrden.
        public async Task<List<CompraAlquilerDTO>> LeerComprasAlquilerAsync(string producto, string numSerie)
        {
            // Se materializa primero y se proyecta en memoria para poder usar ?.Trim() (no
            // traducible por EF) y dejar nombres/columnas limpias en el DTO.
            List<LinPedidoCmp> lineas = await db.LinPedidoCmps
                .Where(l => l.Producto == producto && l.NumSerie == numSerie)
                .OrderBy(l => l.NºOrden)
                .ToListAsync()
                .ConfigureAwait(false);

            return lineas.Select(l => new CompraAlquilerDTO
            {
                NumeroOrden = l.NºOrden,
                NumeroPedido = l.Número,
                Proveedor = l.NºProveedor?.Trim(),
                FechaRecepcion = l.FechaRecepción,
                Producto = l.Producto?.Trim(),
                Texto = l.Texto?.Trim(),
                Cantidad = l.Cantidad,
                Precio = l.Precio,
                Total = l.Total,
                Estado = l.Estado,
                NumSerie = l.NumSerie?.Trim()
            }).ToList();
        }

        // Nesto#340 Fase 1C.2: pestaña "Inmovilizados" de un alquiler (extracto del inmovilizado).
        // Sustituye la lectura EF DbContext.ExtractoInmovilizado del cliente Nesto. La tabla no está
        // mapeada en NestoAPI, así que se lee con SQL crudo aliasando las columnas (NºOrden, Número,
        // NºDocumento llevan acentos/º) a nombres ASCII limpios.
        public async Task<List<ExtractoInmovilizadoDTO>> LeerInmovilizadosAlquilerAsync(string empresa, string numero)
        {
            const string sql =
                "SELECT [NºOrden] AS NumeroOrden, [Fecha] AS Fecha, [Concepto] AS Concepto, " +
                "[NºDocumento] AS NumeroDocumento, [Importe] AS Importe, [ImportePdte] AS ImportePendiente, " +
                "[Estado] AS Estado " +
                "FROM ExtractoInmovilizado WHERE Empresa = @p0 AND [Número] = @p1 ORDER BY [Fecha]";

            List<FilaInmovilizado> filas = await db.Database
                .SqlQuery<FilaInmovilizado>(sql, empresa, numero)
                .ToListAsync()
                .ConfigureAwait(false);

            return filas.Select(f => new ExtractoInmovilizadoDTO
            {
                NumeroOrden = f.NumeroOrden,
                Fecha = f.Fecha,
                Concepto = f.Concepto?.Trim(),
                NumeroDocumento = f.NumeroDocumento?.Trim(),
                Importe = f.Importe,
                ImportePendiente = f.ImportePendiente,
                Estado = f.Estado
            }).ToList();
        }

        // Nesto#340 Fase 1C.3: grid principal de Alquileres. Sustituye la lectura EF
        // DbContext.CabAlquileres del cliente Nesto (filtraba por Producto, ordenaba por NumeroSerie).
        // Se incluyen los datos del cliente (nombre/dirección) para que el cliente pueda imprimir la
        // etiqueta del pedido sin la navegación EF CabAlquileres.Clientes.
        public async Task<List<AlquilerCabeceraDTO>> LeerCabecerasAlquilerAsync(string empresa, string producto)
        {
            List<CabAlquiler> cabeceras = await db.CabAlquileres
                .Where(c => c.Empresa == empresa && c.Producto == producto)
                .OrderBy(c => c.NumeroSerie)
                .ToListAsync()
                .ConfigureAwait(false);

            return await ProyectarConClientesAsync(empresa, cabeceras).ConfigureAwait(false);
        }

        // Nesto#340 Fase 1C.3: guardado del grid (reconcile). Sustituye DbContext.SaveChanges del
        // cliente Nesto, que persistía altas/ediciones/bajas del ChangeTracker. Aquí se reciben las
        // cabeceras actuales del producto y se reconcilian con las que hay en la BD:
        //   - Numero > 0  -> edición de la fila existente
        //   - Numero == 0 -> alta (la identity asigna el Número)
        //   - filas en BD que ya no vienen -> baja
        // SaveChangesAsync es transaccional, así que todo se aplica de forma atómica.
        public async Task<List<AlquilerCabeceraDTO>> GuardarCabecerasAlquilerAsync(string empresa, string producto, List<AlquilerCabeceraDTO> cabeceras, string usuario)
        {
            cabeceras = cabeceras ?? new List<AlquilerCabeceraDTO>();

            List<CabAlquiler> existentes = await db.CabAlquileres
                .Where(c => c.Empresa == empresa && c.Producto == producto)
                .ToListAsync()
                .ConfigureAwait(false);

            HashSet<int> numerosEntrantes = new HashSet<int>(cabeceras.Where(c => c.Numero > 0).Select(c => c.Numero));

            // Bajas: lo que estaba en la BD para este producto y ya no viene en la lista.
            foreach (CabAlquiler existente in existentes.Where(e => !numerosEntrantes.Contains(e.Número)).ToList())
            {
                db.CabAlquileres.Remove(existente);
            }

            DateTime ahora = DateTime.Now;
            foreach (AlquilerCabeceraDTO dto in cabeceras)
            {
                CabAlquiler entidad = dto.Numero > 0
                    ? existentes.FirstOrDefault(e => e.Número == dto.Numero)
                    : null;

                if (entidad == null)
                {
                    entidad = new CabAlquiler { Empresa = empresa };
                    db.CabAlquileres.Add(entidad);
                }

                AplicarDto(entidad, dto, producto);
                entidad.Usuario = usuario;
                entidad.FechaModificación = ahora;
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            // Releemos para devolver los Números asignados a las altas y los datos de cliente.
            return await LeerCabecerasAlquilerAsync(empresa, producto).ConfigureAwait(false);
        }

        private async Task<List<AlquilerCabeceraDTO>> ProyectarConClientesAsync(string empresa, List<CabAlquiler> cabeceras)
        {
            // Cargamos en una sola consulta los clientes referenciados para rellenar los datos de
            // etiqueta (nombre/dirección). Se materializa y se hace el match en memoria con Trim
            // porque las claves de la BD vienen rellenas con espacios (sistema legacy).
            Dictionary<string, Cliente> clientesPorClave = new Dictionary<string, Cliente>();
            List<string> numerosCliente = cabeceras
                .Where(c => !string.IsNullOrWhiteSpace(c.Cliente))
                .Select(c => c.Cliente)
                .Distinct()
                .ToList();

            if (numerosCliente.Any())
            {
                List<Cliente> clientes = await db.Clientes
                    .Where(cli => cli.Empresa == empresa && numerosCliente.Contains(cli.Nº_Cliente))
                    .ToListAsync()
                    .ConfigureAwait(false);

                foreach (Cliente cli in clientes)
                {
                    clientesPorClave[ClaveCliente(cli.Nº_Cliente, cli.Contacto)] = cli;
                }
            }

            return cabeceras.Select(c =>
            {
                Cliente cli = null;
                if (!string.IsNullOrWhiteSpace(c.Cliente))
                {
                    clientesPorClave.TryGetValue(ClaveCliente(c.Cliente, c.Contacto), out cli);
                }
                return MapearADto(c, cli);
            }).ToList();
        }

        private static string ClaveCliente(string cliente, string contacto)
        {
            return (cliente?.Trim() ?? string.Empty) + "|" + (contacto?.Trim() ?? string.Empty);
        }

        private static void AplicarDto(CabAlquiler entidad, AlquilerCabeceraDTO dto, string productoPorDefecto)
        {
            entidad.Cliente = dto.Cliente?.Trim();
            entidad.Contacto = dto.Contacto?.Trim();
            entidad.Producto = string.IsNullOrWhiteSpace(dto.Producto) ? productoPorDefecto : dto.Producto.Trim();
            entidad.Inmovilizado = dto.Inmovilizado?.Trim();
            entidad.Cuotas = dto.Cuotas;
            entidad.FechaEntrega = dto.FechaEntrega;
            entidad.FechaSeñal = dto.FechaSenal;
            entidad.ImporteSeñal = dto.ImporteSenal;
            entidad.NumeroSerie = dto.NumeroSerie?.Trim();
            entidad.SeñalComisiona = dto.SenalComisiona;
            entidad.Indemnización = dto.Indemnizacion;
            entidad.Importe = dto.Importe;
            entidad.CabPedidoVta = dto.CabPedidoVta;
            entidad.RutaContrato = dto.RutaContrato?.Trim();
            entidad.Comentarios = dto.Comentarios;
        }

        private static AlquilerCabeceraDTO MapearADto(CabAlquiler c, Cliente cli)
        {
            return new AlquilerCabeceraDTO
            {
                Empresa = c.Empresa?.Trim(),
                Numero = c.Número,
                Cliente = c.Cliente?.Trim(),
                Contacto = c.Contacto?.Trim(),
                Producto = c.Producto?.Trim(),
                Inmovilizado = c.Inmovilizado?.Trim(),
                Cuotas = c.Cuotas,
                FechaEntrega = c.FechaEntrega,
                FechaSenal = c.FechaSeñal,
                ImporteSenal = c.ImporteSeñal,
                NumeroSerie = c.NumeroSerie?.Trim(),
                SenalComisiona = c.SeñalComisiona,
                Indemnizacion = c.Indemnización,
                Importe = c.Importe,
                CabPedidoVta = c.CabPedidoVta,
                RutaContrato = c.RutaContrato?.Trim(),
                Comentarios = c.Comentarios,
                NombreProducto = c.NombreProducto?.Trim(),
                Familia = c.Familia?.Trim(),
                NombreCliente = cli?.Nombre?.Trim(),
                DireccionCliente = cli?.Dirección?.Trim(),
                CodPostalCliente = cli?.CodPostal?.Trim(),
                PoblacionCliente = cli?.Población?.Trim(),
                ProvinciaCliente = cli?.Provincia?.Trim()
            };
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

        // Tipo crudo del extracto de inmovilizado: nombres = alias del SELECT.
        private class FilaInmovilizado
        {
            public int NumeroOrden { get; set; }
            public System.DateTime Fecha { get; set; }
            public string Concepto { get; set; }
            public string NumeroDocumento { get; set; }
            public decimal Importe { get; set; }
            public decimal ImportePendiente { get; set; }
            public short Estado { get; set; }
        }
    }
}
