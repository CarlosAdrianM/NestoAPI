﻿using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    public interface ISelectorRecordatoriosReposicion
    {
        /// <summary>
        /// Cálculo en seco (NestoAPI#532): a qué clientes se les recordaría hoy reponer qué productos.
        /// Solo lee: no manda nada ni registra nada.
        /// </summary>
        Task<ResultadoRecordatorioReposicionDTO> Calcular(string empresa, DateTime hoy, string consumibles = null);

        /// <summary>
        /// NestoAPI#662: el mismo cálculo, pero SOLO para esos clientes (una consulta acotada por Nº Cliente
        /// para cada uno) y para el canal push: sin exigir correo y sin grupo de control.
        /// </summary>
        Task<ResultadoRecordatorioReposicionDTO> CalcularParaClientes(string empresa, DateTime hoy, string consumibles,
            IReadOnlyCollection<string> clientes);

        /// <summary>
        /// NestoAPI#661: los productos que ha comprado UN cliente, con lo que le toca reponer arriba
        /// (<see cref="CalculadoraReposicion.Habituales"/>).
        /// </summary>
        Task<List<HabitualClienteDTO>> LeerHabituales(string empresa, string cliente, DateTime hoy, string consumibles = null);
    }

    /// <summary>
    /// NestoAPI#532: lee de la BD lo que necesita <see cref="CalculadoraReposicion"/>.
    ///
    /// CUIDADO CON LA CARGA: la consulta de compras recorre 24 meses de LinPedidoVta (unos 2,7 millones de
    /// filas en total). Está escrita para que la cubra ENTERA el índice
    /// <c>_dta_index_LinPedidoVta_..._K7_K4_K1_K35_11_16_21_23_31</c> (Producto, Nº Cliente, Empresa, Estado
    /// + Cantidad, Base Imponible y Fecha Albarán incluidas): un recorrido de ese índice, sin búsquedas en
    /// la tabla. Medido el 25/09/26: ~41.000 lecturas lógicas, ~3 s, 170.000 filas (cliente, producto,
    /// día). NO añadir columnas ni condiciones que no estén en ese índice (TipoLinea, Grupo, Familia...):
    /// cada fila haría una búsqueda en el índice agrupado y la consulta pasaría de segundos a minutos.
    /// El filtro de grupo/subgrupo se hace en memoria con la ficha del producto.
    ///
    /// NestoAPI#661/#662: la misma lectura acotada a un cliente (<see cref="SQL_COMPRAS_CLIENTE"/>) es la que
    /// usan el endpoint de habituales (bajo demanda, en horario) y la push diaria: hay índices de LinPedidoVta
    /// que empiezan por Nº Cliente, así que es una búsqueda, no un recorrido. El parámetro va como varchar
    /// (la columna es char): con nvarchar la conversión implícita podría echar a perder la búsqueda.
    /// Medido el 16/10/26 con un cliente real (196 filas cliente-producto-día): compras 14 lecturas lógicas y
    /// 2 ms; pendientes 17 lecturas lógicas y 1 ms.
    /// </summary>
    public class SelectorRecordatoriosReposicion : ISelectorRecordatoriosReposicion
    {
        private const string SQL_COMPRAS_SELECT = @"
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto,
       CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS Dia, SUM(l.[Base Imponible]) AS BaseImponible,
       CAST(SUM(l.Cantidad) AS int) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Estado >= 2 AND l.Cantidad > 0 AND l.Producto IS NOT NULL
  AND l.[Fecha Albarán] >= @desde AND l.[Fecha Albarán] < @hasta";

        private const string SQL_COMPRAS_GROUP = @"
GROUP BY l.[Nº Cliente], l.Producto, CAST(l.[Fecha Albarán] AS date)";

        internal const string SQL_COMPRAS = SQL_COMPRAS_SELECT + SQL_COMPRAS_GROUP;

        /// <summary>NestoAPI#661: lo mismo, de un solo cliente.</summary>
        internal const string SQL_COMPRAS_CLIENTE = SQL_COMPRAS_SELECT + @"
  AND l.[Nº Cliente] = @cliente" + SQL_COMPRAS_GROUP;

        // Pedidos pendientes/en curso (pocos cientos de líneas) y notas de entrega de los últimos 24 meses.
        // Estado es la clave del índice agrupado: es una búsqueda por rango, no un recorrido.
        internal const string SQL_PENDIENTES = @"
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto, l.Estado, l.[Fecha Entrega] AS Fecha
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Estado BETWEEN -2 AND 1 AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Producto IS NOT NULL
  AND (l.Estado >= -1 OR l.[Fecha Entrega] >= @desde)";

        /// <summary>NestoAPI#661: lo mismo, de un solo cliente.</summary>
        internal const string SQL_PENDIENTES_CLIENTE = SQL_PENDIENTES + @"
  AND l.[Nº Cliente] = @cliente";

        private const int TAMANO_LOTE_CLIENTES = 500;
        private const int TAMANO_LOTE_PRODUCTOS = 500;

        private readonly NVEntities db;
        private readonly Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock;

        public SelectorRecordatoriosReposicion(NVEntities db, Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock = null)
        {
            this.db = db;
            this.leerSinStock = leerSinStock ?? LeerSinStockBd;
        }

        public async Task<ResultadoRecordatorioReposicionDTO> Calcular(string empresa, DateTime hoy, string consumibles = null)
        {
            DateTime fechaHoy = hoy.Date;
            DateTime desde = CalculadoraReposicion.HistorialDesde(fechaHoy);

            List<CompraDiaReposicion> compras = await LeerCompras(empresa, desde, fechaHoy, null).ConfigureAwait(false);
            List<LineaPendienteReposicion> pendientes = await LeerPendientes(empresa, desde, null).ConfigureAwait(false);
            // Toda la tabla de productos (pocas decenas de miles de filas estrechas): más barato que un
            // Contains con miles de referencias.
            Dictionary<string, ProductoReposicion> productos = await LeerProductos(empresa, null).ConfigureAwait(false);

            return await Agrupar(empresa, fechaHoy, consumibles, compras, pendientes, productos,
                exigirCorreo: true, porcentajeGrupoControl: CalculadoraReposicion.PORCENTAJE_GRUPO_CONTROL).ConfigureAwait(false);
        }

        public async Task<ResultadoRecordatorioReposicionDTO> CalcularParaClientes(string empresa, DateTime hoy, string consumibles,
            IReadOnlyCollection<string> clientes)
        {
            DateTime fechaHoy = hoy.Date;
            DateTime desde = CalculadoraReposicion.HistorialDesde(fechaHoy);
            var compras = new List<CompraDiaReposicion>();
            var pendientes = new List<LineaPendienteReposicion>();
            foreach (string cliente in (clientes ?? new List<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                compras.AddRange(await LeerCompras(empresa, desde, fechaHoy, cliente).ConfigureAwait(false));
                pendientes.AddRange(await LeerPendientes(empresa, desde, cliente).ConfigureAwait(false));
            }
            Dictionary<string, ProductoReposicion> productos = await LeerProductos(empresa, ProductosDe(compras, pendientes))
                .ConfigureAwait(false);

            return await Agrupar(empresa, fechaHoy, consumibles, compras, pendientes, productos,
                exigirCorreo: false, porcentajeGrupoControl: 0).ConfigureAwait(false);
        }

        public async Task<List<HabitualClienteDTO>> LeerHabituales(string empresa, string cliente, DateTime hoy, string consumibles = null)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return new List<HabitualClienteDTO>();
            }
            DateTime fechaHoy = hoy.Date;
            DateTime desde = CalculadoraReposicion.HistorialDesde(fechaHoy);
            List<CompraDiaReposicion> compras = await LeerCompras(empresa, desde, fechaHoy, cliente.Trim()).ConfigureAwait(false);
            if (compras.Count == 0)
            {
                return new List<HabitualClienteDTO>();
            }
            List<LineaPendienteReposicion> pendientes = await LeerPendientes(empresa, desde, cliente.Trim()).ConfigureAwait(false);
            Dictionary<string, ProductoReposicion> productos = await LeerProductos(empresa, ProductosDe(compras, pendientes))
                .ConfigureAwait(false);

            return CalculadoraReposicion.Habituales(compras, pendientes, productos, ConsumiblesReposicion.Leer(consumibles),
                fechaHoy, leerSinStock);
        }

        private async Task<ResultadoRecordatorioReposicionDTO> Agrupar(string empresa, DateTime fechaHoy, string consumibles,
            List<CompraDiaReposicion> compras, List<LineaPendienteReposicion> pendientes,
            Dictionary<string, ProductoReposicion> productos, bool exigirCorreo, int porcentajeGrupoControl)
        {
            ConsumiblesReposicion listaConsumibles = ConsumiblesReposicion.Leer(consumibles);
            List<CandidatoReposicionDTO> candidatos = CalculadoraReposicion.EvaluarPares(compras, pendientes, productos,
                listaConsumibles, fechaHoy);

            List<string> productosAvisables = candidatos.Where(c => c.SeAvisaria)
                .Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (productosAvisables.Any())
            {
                CalculadoraReposicion.AplicarSinStock(candidatos, leerSinStock(productosAvisables));
            }

            List<string> clientesAvisables = candidatos.Where(c => c.SeAvisaria)
                .Select(c => c.Cliente).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, ClienteReposicion> clientes = await LeerClientes(empresa, clientesAvisables).ConfigureAwait(false);

            return CalculadoraReposicion.Agrupar(candidatos, clientes, fechaHoy, listaConsumibles.Definicion,
                exigirCorreo, porcentajeGrupoControl);
        }

        private static List<string> ProductosDe(IEnumerable<CompraDiaReposicion> compras, IEnumerable<LineaPendienteReposicion> pendientes)
            => compras.Select(c => c.Producto)
                .Concat(pendientes.Select(p => p.Producto))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Compras por día (albarán) desde <paramref name="desde"/> hasta hoy; con cliente, solo las suyas.</summary>
        private async Task<List<CompraDiaReposicion>> LeerCompras(string empresa, DateTime desde, DateTime fechaHoy, string cliente)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@desde", desde),
                new SqlParameter("@hasta", fechaHoy.AddDays(1))
            };
            if (cliente != null)
            {
                parametros.Add(ParametroCliente(cliente));
            }
            return await db.Database.SqlQuery<CompraDiaReposicion>(cliente == null ? SQL_COMPRAS : SQL_COMPRAS_CLIENTE,
                    parametros.Cast<object>().ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        private async Task<List<LineaPendienteReposicion>> LeerPendientes(string empresa, DateTime desde, string cliente)
        {
            var parametros = new List<SqlParameter>
            {
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@desde", desde)
            };
            if (cliente != null)
            {
                parametros.Add(ParametroCliente(cliente));
            }
            return await db.Database.SqlQuery<LineaPendienteReposicion>(cliente == null ? SQL_PENDIENTES : SQL_PENDIENTES_CLIENTE,
                    parametros.Cast<object>().ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        private static SqlParameter ParametroCliente(string cliente)
            => new SqlParameter("@cliente", SqlDbType.VarChar, 10) { Value = cliente.Trim() };

        /// <summary>Fichas de producto. Sin lista, la tabla entera; con lista, por lotes.</summary>
        private async Task<Dictionary<string, ProductoReposicion>> LeerProductos(string empresa, List<string> ids)
        {
            var fichas = new List<ProductoReposicion>();
            if (ids == null)
            {
                fichas = await ConsultaProductos(empresa).ToListAsync().ConfigureAwait(false);
            }
            else
            {
                for (int i = 0; i < ids.Count; i += TAMANO_LOTE_PRODUCTOS)
                {
                    List<string> lote = ids.Skip(i).Take(TAMANO_LOTE_PRODUCTOS).ToList();
                    fichas.AddRange(await ConsultaProductos(empresa)
                        .Where(p => lote.Contains(p.Producto))
                        .ToListAsync().ConfigureAwait(false));
                }
            }
            return fichas
                .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        private IQueryable<ProductoReposicion> ConsultaProductos(string empresa)
            => db.Productos
                .Where(p => p.Empresa == empresa)
                .Select(p => new ProductoReposicion
                {
                    Producto = p.Número,
                    Nombre = p.Nombre,
                    Grupo = p.Grupo,
                    SubGrupo = p.SubGrupo,
                    Familia = p.Familia,
                    Estado = p.Estado ?? 0,
                    Ficticio = p.Ficticio
                });

        /// <summary>
        /// Ficha principal, correo (el primero no vacío de sus personas de contacto, como #74), nombre
        /// del vendedor y vendedor de peluquería (subconsulta en la misma consulta del lote). Por lotes para que el IN no se dispare.
        /// </summary>
        private async Task<Dictionary<string, ClienteReposicion>> LeerClientes(string empresa, List<string> ids)
        {
            var resultado = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ids.Count; i += TAMANO_LOTE_CLIENTES)
            {
                List<string> lote = ids.Skip(i).Take(TAMANO_LOTE_CLIENTES).ToList();

                var fichas = await db.Clientes
                    .Where(c => c.Empresa == empresa && c.ClientePrincipal && lote.Contains(c.Nº_Cliente))
                    .Select(c => new
                    {
                        c.Nº_Cliente,
                        c.Nombre,
                        c.Estado,
                        c.Vendedor,
                        // NestoAPI#532: el de peluquería, del contacto principal (como RepositorioSugerenciasProductos)
                        VendedorPeluqueria = db.VendedoresClientesGruposProductos
                            .Where(v => v.Empresa == c.Empresa && v.Cliente == c.Nº_Cliente && v.Contacto == c.Contacto
                                && v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)
                            .Select(v => v.Vendedor)
                            .FirstOrDefault(),
                        VendedorNombre = db.Vendedores
                            .Where(v => v.Empresa == c.Empresa && v.Número == c.Vendedor)
                            .Select(v => v.Descripción)
                            .FirstOrDefault()
                    })
                    .ToListAsync().ConfigureAwait(false);

                var correos = await db.PersonasContactoClientes
                    // Carlos (25/09/26): es un correo comercial: solo a quien tiene marcado «Enviar boletín»
                    .Where(p => p.Empresa == empresa && lote.Contains(p.NºCliente) && p.EnviarBoletin
                        && p.CorreoElectrónico != null && p.CorreoElectrónico.Trim() != "")
                    .GroupBy(p => p.NºCliente)
                    .Select(g => new { Cliente = g.Key, Email = g.FirstOrDefault().CorreoElectrónico })
                    .ToListAsync().ConfigureAwait(false);
                Dictionary<string, string> correoPorCliente = correos
                    .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Email?.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var ficha in fichas)
                {
                    string id = ficha.Nº_Cliente.Trim();
                    if (resultado.ContainsKey(id))
                    {
                        continue;
                    }
                    resultado[id] = new ClienteReposicion
                    {
                        Cliente = id,
                        Nombre = ficha.Nombre?.Trim(),
                        Estado = ficha.Estado ?? 0,
                        Vendedor = ficha.Vendedor?.Trim(),
                        VendedorNombre = ficha.VendedorNombre?.Trim(),
                        VendedorPeluqueria = string.IsNullOrWhiteSpace(ficha.VendedorPeluqueria) ? null : ficha.VendedorPeluqueria.Trim(),
                        Email = correoPorCliente.TryGetValue(id, out string email) ? email : null
                    };
                }
            }
            return resultado;
        }

        private static ISet<string> LeerSinStockBd(IReadOnlyCollection<string> productos)
        {
            ResumenStocksProductos resumen = new ServicioGestorStocks().LeerResumenStocks(productos);
            return CalculadoraReposicion.ProductosSinStock(resumen, productos);
        }
    }
}
