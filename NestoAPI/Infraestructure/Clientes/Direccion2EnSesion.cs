using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Linq;
using System.Threading;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#874 (19/11/26, caso 42154/0): el correo «Informe de Cambios de Vendedores» lo manda el disparador trgClientesUpd en el INSERT de
    /// la ficha, y la Dirección 2 (fuera del EDMX) se escribe DESPUÉS, en un UPDATE aparte (<see cref="Direccion2ClienteSql.Guardar"/>): en el
    /// INSERT, <c>inserted.Direccion2</c> es NULL y el correo salía sin ella. Antes del INSERT, el alta deja la Dirección 2 de cada ficha en el
    /// contexto de la sesión de SQL (<c>sp_set_session_context</c>, SQL Server 2016+) con la clave <see cref="Clave"/>, y el disparador
    /// (Scripts/Issue874_trgClientesUpd_Direccion2.sql) usa <c>ISNULL(i.Direccion2, SESSION_CONTEXT(clave))</c>.
    /// <list type="bullet">
    /// <item>Tiene que ser la MISMA sesión que el INSERT: EF abre y cierra la conexión en cada SaveChanges y, al volver al pool, la sesión se
    /// reinicia (sp_reset_connection borra el contexto). Por eso se abre la conexión aquí (si estaba cerrada) y se deja abierta hasta el
    /// <see cref="IDisposable.Dispose"/>, que borra las claves y la cierra si la abrió. Dentro de la transacción del alta con dirección de
    /// entrega (#879) la conexión ya está abierta y es la de la transacción.</item>
    /// <item>Nunca lanza ni rompe el alta: si falla, ELMAH una vez por proceso y el correo sale como antes (sin la Dirección 2).</item>
    /// <item>Sin las columnas (sin Scripts/Issue874_ClientesDireccion2.sql) no hace nada.</item>
    /// </list>
    /// </summary>
    public static class Direccion2EnSesion
    {
        public const string PREFIJO = "Direccion2Alta:";
        /// <summary>sp_set_session_context: la clave es sysname (128).</summary>
        public const int LARGO_MAXIMO_CLAVE = 128;

        internal const string SQL_PONER = "EXEC sys.sp_set_session_context @key = @clave, @value = @valor";

        private static int avisado;

        /// <summary>
        /// La clave de una ficha, la MISMA que compone el disparador:
        /// <c>N'Direccion2Alta:' + LTRIM(RTRIM(Empresa)) + N'|' + LTRIM(RTRIM([Nº Cliente])) + N'|' + LTRIM(RTRIM(Contacto))</c>. Puro.
        /// </summary>
        public static string Clave(string empresa, string cliente, string contacto)
        {
            string clave = $"{PREFIJO}{(empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim()}|{cliente?.Trim()}|{(contacto ?? "0").Trim()}";
            return clave.Length > LARGO_MAXIMO_CLAVE ? clave.Substring(0, LARGO_MAXIMO_CLAVE) : clave;
        }

        /// <summary>Lo que se deja en la sesión: ficha → su Dirección 2 (solo las que la tienen). Puro.</summary>
        public static List<KeyValuePair<string, string>> Entradas(IEnumerable<(Cliente Ficha, ResultadoDireccion2 Direccion2)> fichas)
        {
            return (fichas ?? Enumerable.Empty<(Cliente, ResultadoDireccion2)>())
                .Where(f => f.Ficha != null && !string.IsNullOrWhiteSpace(f.Ficha.Nº_Cliente) && f.Direccion2 != null && f.Direccion2.Valida
                            && !string.IsNullOrWhiteSpace(f.Direccion2.Direccion2))
                .Select(f => new KeyValuePair<string, string>(Clave(f.Ficha.Empresa, f.Ficha.Nº_Cliente, f.Ficha.Contacto), f.Direccion2.Direccion2.Trim()))
                .ToList();
        }

        /// <summary>
        /// Deja las entradas en la sesión de la conexión de <paramref name="db"/> (abriéndola si hace falta) hasta el Dispose. Nunca lanza:
        /// si algo falla, devuelve un Dispose que no hace nada (o solo cierra lo que abrió).
        /// </summary>
        public static IDisposable Poner(NVEntities db, IReadOnlyList<KeyValuePair<string, string>> entradas)
        {
            if (db == null || entradas == null || !entradas.Any())
            {
                return Nada.Instancia;
            }
            var ambito = new Ambito(db, entradas);
            try
            {
                if (!ColumnasOpcionales.Existen(db, Direccion2ClienteSql.TABLA, Direccion2ClienteSql.COLUMNA_DIRECCION2, Direccion2ClienteSql.COLUMNA_TIPO_LUGAR))
                {
                    return Nada.Instancia;
                }
                ambito.Abrir();
                foreach (KeyValuePair<string, string> entrada in entradas)
                {
                    ambito.Fijar(entrada.Key, entrada.Value);
                }
                return ambito;
            }
            catch (Exception ex)
            {
                Avisar(ex);
                ambito.Dispose();
                return Nada.Instancia;
            }
        }

        /// <summary>Llama a <paramref name="poner"/> sin dejar que un fallo rompa el alta.</summary>
        public static IDisposable Seguro(Func<IDisposable> poner)
        {
            try
            {
                return poner?.Invoke() ?? Nada.Instancia;
            }
            catch (Exception ex)
            {
                Avisar(ex);
                return Nada.Instancia;
            }
        }

        private static void Avisar(Exception ex)
        {
            if (Interlocked.Exchange(ref avisado, 1) != 0)
            {
                return;
            }
            try
            {
                ElmahHelper.Log(new Exception($"[Clientes #874] No se ha podido dejar la dirección 2 en la sesión de SQL para el correo del alta " +
                    $"(el correo sale sin ella; el alta sigue). Solo se avisa una vez por proceso: {ex.Message}", ex));
            }
            catch
            {
                // nunca romper el alta
            }
        }

        internal static void ReiniciarAvisoParaTests() => Interlocked.Exchange(ref avisado, 0);

        private sealed class Nada : IDisposable
        {
            public static readonly Nada Instancia = new Nada();
            public void Dispose() { }
        }

        private sealed class Ambito : IDisposable
        {
            private readonly NVEntities db;
            private readonly IReadOnlyList<KeyValuePair<string, string>> entradas;
            private DbConnection conexion;
            private bool abiertaAqui;
            private bool cerrado;

            public Ambito(NVEntities db, IReadOnlyList<KeyValuePair<string, string>> entradas)
            {
                this.db = db;
                this.entradas = entradas;
            }

            public void Abrir()
            {
                conexion = db.Database.Connection;
                if (conexion.State != ConnectionState.Open)
                {
                    conexion.Open();
                    abiertaAqui = true;
                }
            }

            public void Fijar(string clave, string valor)
            {
                // Sin transacción propia: el contexto de sesión no es transaccional y dentro de la del alta va en ella.
                _ = db.Database.ExecuteSqlCommand(TransactionalBehavior.DoNotEnsureTransaction, SQL_PONER,
                    new System.Data.SqlClient.SqlParameter("@clave", SqlDbType.NVarChar, LARGO_MAXIMO_CLAVE) { Value = clave },
                    new System.Data.SqlClient.SqlParameter("@valor", SqlDbType.NVarChar, 100) { Value = (object)valor ?? DBNull.Value });
            }

            public void Dispose()
            {
                if (cerrado)
                {
                    return;
                }
                cerrado = true;
                try
                {
                    if (conexion != null && conexion.State == ConnectionState.Open)
                    {
                        foreach (KeyValuePair<string, string> entrada in entradas)
                        {
                            Fijar(entrada.Key, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Avisar(ex);
                }
                finally
                {
                    try
                    {
                        if (abiertaAqui && conexion != null && conexion.State == ConnectionState.Open)
                        {
                            conexion.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        Avisar(ex);
                    }
                }
            }
        }
    }
}
