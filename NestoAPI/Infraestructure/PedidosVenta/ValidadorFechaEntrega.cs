using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#684: único sitio que decide si una fecha de entrega de una línea de pedido de venta es válida
    /// (no anterior a <see cref="Constantes.Pedidos.FECHA_ENTREGA_MINIMA"/>, el 01/01/2000). Pedido 928623: al corregir
    /// 19/10/2027 se grabó 19/10/1926 en las 12 líneas y se sirvieron al día siguiente; con 1927 por 2027 se habría servido
    /// algo que no se quería servir todavía. Las fechas futuras (2099...) siguen valiendo: retienen líneas.
    /// <para>Dos puntos de uso: <see cref="Validar(IEnumerable{LineaPedidoVentaDTO})"/> en la entrada del POST y el PUT de
    /// pedidos (400 con el mensaje, antes de tocar nada), y <see cref="Comprobar(LinPedidoVta)"/> como red de seguridad
    /// en <c>NVEntities.SaveChanges</c> para cualquier otro camino que cree o cambie la fecha de una línea.</para>
    /// </summary>
    public static class ValidadorFechaEntrega
    {
        /// <summary>Código del error (400) de <see cref="PedidoInvalidoException"/>.</summary>
        public const string CODIGO_FECHA_ENTREGA_NO_VALIDA = "FECHA_ENTREGA_NO_VALIDA";

        public static bool EsValida(DateTime? fechaEntrega)
        {
            return fechaEntrega == null || fechaEntrega.Value >= Constantes.Pedidos.FECHA_ENTREGA_MINIMA;
        }

        /// <summary>«La fecha de entrega del producto 12345 (19/10/1926) no es válida: no puede ser anterior al 01/01/2000. Revise el año.»</summary>
        public static string Mensaje(string producto, DateTime fechaEntrega)
        {
            string queLinea = string.IsNullOrWhiteSpace(producto) ? "de una línea" : $"del producto {producto.Trim()}";
            return $"La fecha de entrega {queLinea} ({Formatear(fechaEntrega)}) no es válida: " +
                $"no puede ser anterior al {Formatear(Constantes.Pedidos.FECHA_ENTREGA_MINIMA)}. Revise el año.";
        }

        /// <summary>
        /// Las líneas que trae un pedido (POST o PUT). Null si todas valen; si no, el mensaje de la primera que no.
        /// No se miran: la fecha sin informar (<c>default(DateTime)</c>, que CrearLineaVta convierte en la primera fecha
        /// posible) ni las líneas ya en albarán o factura, que el PUT conserva tal cual (hay 17 históricas con 1899-1997).
        /// </summary>
        public static string Validar(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            if (lineas == null)
            {
                return null;
            }
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                if (linea == null || linea.estado >= Constantes.EstadosLineaVenta.ALBARAN || linea.fechaEntrega == default)
                {
                    continue;
                }
                if (!EsValida(linea.fechaEntrega))
                {
                    return Mensaje(linea.Producto, linea.fechaEntrega);
                }
            }
            return null;
        }

        /// <summary>Lanza <see cref="PedidoInvalidoException"/> (400) si la línea va a grabarse con una fecha no válida.</summary>
        public static void Comprobar(LinPedidoVta linea)
        {
            if (linea == null || EsValida(linea.Fecha_Entrega))
            {
                return;
            }
            throw new PedidoInvalidoException(Mensaje(linea.Producto, linea.Fecha_Entrega),
                CODIGO_FECHA_ENTREGA_NO_VALIDA, empresa: linea.Empresa?.Trim(), pedido: linea.Número,
                cliente: linea.Nº_Cliente?.Trim(), usuario: linea.Usuario?.Trim());
        }

        /// <summary>
        /// Red de seguridad de <c>NVEntities.SaveChanges</c>: se comprueban las líneas nuevas y las modificadas a las que
        /// se les ha cambiado la fecha. Una línea que se modifica sin tocar la fecha (un traspaso, una agrupación) no se
        /// mira: hay líneas históricas facturadas con fechas de 1899-1997 que el CHECK (WITH NOCHECK) también respeta.
        /// </summary>
        internal static bool HayQueComprobarAlGuardar(EntityState estado, Func<bool> fechaModificada)
        {
            return estado == EntityState.Added || (estado == EntityState.Modified && fechaModificada());
        }

        private static string Formatear(DateTime fecha)
        {
            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
