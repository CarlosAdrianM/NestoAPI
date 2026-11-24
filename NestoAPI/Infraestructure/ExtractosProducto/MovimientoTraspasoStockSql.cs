using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ExtractosProducto
{
    /// <summary>Una línea de un movimiento de stock con número de traspaso: + entra en el almacén, − sale.</summary>
    public class LineaMovimientoTraspaso
    {
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>PreExtrProducto.Texto (se recorta a 50).</summary>
        public string Texto { get; set; }
    }

    /// <summary>
    /// El NÚCLEO de los movimientos de stock de la API que no son de un documento (ajustes de diferencias de reposición #634, «No está»
    /// de las tiendas, traspasos con los almacenes ficticios #894): dentro de la transacción del llamante, cada línea va a
    /// PreExtrProducto con su diario de sistema y el NºTraspaso y el diario se contabiliza por el ÚNICO punto de llamada a
    /// prdExtrProducto (<see cref="IServicioExtractoProducto"/>). Como prdExtrProducto contabiliza el diario ENTERO, lo que haya de
    /// otros traspasos se aparta antes y se devuelve después (NestoAPI#553). Delegación ALG y forma de venta VAR, como los apuntes de
    /// _ErrRepo de siempre. No toca Ubicaciones: eso lo hace el llamante por la puerta única (<see cref="Ubicaciones.IPuertaUbicaciones"/>).
    /// </summary>
    public static class MovimientoTraspasoStockSql
    {
        internal const string SQL_GRUPO = @"
SELECT RTRIM(Grupo) FROM Productos WHERE Empresa = @p0 AND [Número] = @p1";

        internal const string SQL_APUNTE = @"
SELECT MAX([Nº Orden]) FROM ExtractoProducto
WHERE Empresa = @p0 AND Diario = @p1 AND [NºTraspaso] = @p2 AND [Número] = @p3 AND [Almacén] = @p4 AND Cantidad = @p5";

        /// <summary>
        /// Contabiliza las líneas en <paramref name="diario"/> con el traspaso <paramref name="traspaso"/>. Devuelve, en el mismo orden,
        /// el ExtractoProducto.[Nº Orden] de cada apunte (null si no se encuentra). Lanza NestoBusinessException si un producto no existe,
        /// una cantidad no cabe en smallint o el procedimiento no contabiliza.
        /// </summary>
        public static async Task<List<int?>> Contabilizar(NVEntities db, IServicioExtractoProducto extractos, string empresa, string diario,
            int traspaso, IReadOnlyList<LineaMovimientoTraspaso> lineas, string usuario)
        {
            var apuntes = new List<int?>();
            List<LineaMovimientoTraspaso> validas = (lineas ?? new List<LineaMovimientoTraspaso>()).Where(l => l != null && l.Cantidad != 0).ToList();
            if (validas.Count == 0)
            {
                return apuntes;
            }
            var preLineas = new List<PreExtrProducto>();
            foreach (LineaMovimientoTraspaso linea in validas)
            {
                if (Math.Abs(linea.Cantidad) > short.MaxValue)
                {
                    throw new NestoBusinessException($"No se pueden mover {linea.Cantidad} unidades de {linea.Producto} en una línea.");
                }
                string grupo = await db.Database.SqlQuery<string>(SQL_GRUPO, Char("@p0", empresa, 3), Char("@p1", linea.Producto, 15))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (grupo == null)
                {
                    throw new NestoBusinessException($"El producto {linea.Producto} no existe: no se puede mover su stock.");
                }
                preLineas.Add(new PreExtrProducto
                {
                    Empresa = empresa,
                    Diario = diario,
                    Número = linea.Producto?.Trim(),
                    Fecha = DateTime.Now,
                    Texto = linea.Texto,
                    Almacén = linea.Almacen?.Trim(),
                    Grupo = grupo,
                    Cantidad = (short)linea.Cantidad,
                    Delegación = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                    Forma_Venta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                    Asiento_Automático = true,
                    Estado = 0,
                    NºTraspaso = traspaso,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }

            List<int> apartadas = await ApartadoTraspasosSql.Apartar(db, empresa, diario, traspaso).ConfigureAwait(false);
            _ = await extractos.CrearLineas(db, preLineas).ConfigureAwait(false);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            int resultado = await extractos.ContabilizarDiario(db, empresa, diario, usuario).ConfigureAwait(false);
            if (resultado <= 0)
            {
                throw new NestoBusinessException($"prdExtrProducto no ha contabilizado el diario {diario} (devuelve {resultado}).");
            }
            foreach (PreExtrProducto preLinea in preLineas)
            {
                db.Entry(preLinea).State = EntityState.Detached; // el procedimiento ya las ha borrado de PreExtrProducto
            }
            await ApartadoTraspasosSql.Devolver(db, empresa, diario, apartadas).ConfigureAwait(false);

            foreach (LineaMovimientoTraspaso linea in validas)
            {
                apuntes.Add(await db.Database.SqlQuery<int?>(SQL_APUNTE,
                    Char("@p0", empresa, 3), Char("@p1", diario, 10),
                    new SqlParameter("@p2", SqlDbType.Int) { Value = traspaso },
                    Char("@p3", linea.Producto, 15), Char("@p4", linea.Almacen, 3),
                    new SqlParameter("@p5", SqlDbType.SmallInt) { Value = (short)linea.Cantidad })
                    .FirstOrDefaultAsync().ConfigureAwait(false));
            }
            return apuntes;
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor?.Trim() ?? DBNull.Value };
        }
    }
}
