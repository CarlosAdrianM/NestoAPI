using NestoAPI.Models;
using System;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#716 (2.3): cuándo, quién y desde qué aplicación se dio de alta cada cliente (Clientes.FechaAlta,
    /// UsuarioAlta y AplicacionAlta; hoy no existe nada parecido). Para los leads de Meta (#623), el abandono y las
    /// campañas. Las columnas las crea Scripts/Issue716_DatosParaModelos.sql, que además rellena el histórico de forma
    /// APROXIMADA (primer pedido o primer rapport) con AplicacionAlta = «Backfill»; no están en el EDMX y se escriben con
    /// SQL plano tras el alta. Best-effort: sin el script no hace nada; con error, el cliente ya está creado y queda en ELMAH.
    /// </summary>
    public static class DatosAltaCliente
    {
        public const string APLICACION_NESTO = "Nesto";
        public const string APLICACION_NESTOAPP = "NestoApp";
        public const string APLICACION_TIENDA_ONLINE = "TiendaOnline";
        public const string APLICACION_DESCONOCIDA = "Desconocida";
        /// <summary>El relleno aproximado del script.</summary>
        public const string APLICACION_BACKFILL = "Backfill";
        public const int LONGITUD_APLICACION = 20;
        public const int LONGITUD_USUARIO = 100;

        internal const string TABLA = "Clientes";
        internal const string COLUMNA_FECHA = "FechaAlta";
        internal const string COLUMNA_USUARIO = "UsuarioAlta";
        internal const string COLUMNA_APLICACION = "AplicacionAlta";

        // Solo si no tiene fecha: un alta nunca pisa otra (p. ej. un contacto nuevo de un cliente del backfill sí la lleva
        // en blanco y se rellena; una ficha ya fechada no se toca).
        internal const string SQL_REGISTRAR = @"
            UPDATE dbo.Clientes SET FechaAlta = @p3, UsuarioAlta = @p4, AplicacionAlta = @p5
            WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Contacto = @p2 AND FechaAlta IS NULL";

        /// <summary>
        /// La aplicación que da el alta. Si el cliente lo manda (ClienteCrear.AplicacionAlta) se respeta; si no, se deduce de
        /// cómo se autenticó: Nesto entra con la cuenta de Windows (DOMINIO\usuario), la tienda con el correo del cliente y
        /// NestoApp con el usuario del Identity.
        /// </summary>
        public static string DeducirAplicacion(IPrincipal user, string explicita)
        {
            string limpia = explicita?.Trim();
            if (!string.IsNullOrEmpty(limpia))
            {
                return limpia.Length > LONGITUD_APLICACION ? limpia.Substring(0, LONGITUD_APLICACION) : limpia;
            }
            string nombre = user?.Identity?.IsAuthenticated == true ? user.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return APLICACION_DESCONOCIDA;
            }
            if (nombre.Contains("\\"))
            {
                return APLICACION_NESTO;
            }
            if (nombre.Contains("@"))
            {
                return APLICACION_TIENDA_ONLINE;
            }
            return APLICACION_NESTOAPP;
        }

        /// <summary>Guarda los datos del alta del cliente recién creado. Llamar DESPUÉS de SaveChanges. True si ha escrito.</summary>
        public static bool Registrar(NVEntities db, Cliente cliente, string usuario, string aplicacion, DateTime ahora)
        {
            if (db == null || cliente == null || string.IsNullOrWhiteSpace(cliente.Nº_Cliente))
            {
                return false;
            }
            if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNA_FECHA, COLUMNA_USUARIO, COLUMNA_APLICACION))
            {
                return false;
            }
            try
            {
                string quien = RegistroMotivoCambioEstado.Recortar(usuario, LONGITUD_USUARIO) ?? UsuarioAuditoriaHelper.DESCONOCIDO;
                string desde = RegistroMotivoCambioEstado.Recortar(aplicacion, LONGITUD_APLICACION) ?? APLICACION_DESCONOCIDA;
                int filas = db.Database.ExecuteSqlCommand(SQL_REGISTRAR,
                    (cliente.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim(), cliente.Nº_Cliente.Trim(), (cliente.Contacto ?? "0").Trim(),
                    ahora, quien, desde);
                return filas > 0;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes #716] No se han podido guardar los datos de alta del cliente {cliente.Nº_Cliente.Trim()}/{cliente.Contacto?.Trim()}: {ex.Message}", ex));
                return false;
            }
        }
    }
}
