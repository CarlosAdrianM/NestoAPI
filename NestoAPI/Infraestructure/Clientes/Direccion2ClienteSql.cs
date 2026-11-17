using NestoAPI.Models;
using System;
using System.Linq;
using System.Threading;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#874: Clientes.Direccion2 y Clientes.TipoLugar, columnas fuera del EDMX (Scripts/Issue874_ClientesDireccion2.sql)
    /// que se leen y escriben con SQL plano (como los datos del alta de #716). Sin el script todo sigue como antes: no se guarda
    /// ni se lee nada y se apunta UNA vez por proceso en ELMAH. Nunca lanza: el cliente ya está creado y un envío no se para.
    /// </summary>
    public static class Direccion2ClienteSql
    {
        internal const string TABLA = "Clientes";
        internal const string COLUMNA_DIRECCION2 = "Direccion2";
        internal const string COLUMNA_TIPO_LUGAR = "TipoLugar";

        internal const string SQL_GUARDAR = @"
            UPDATE dbo.Clientes SET Direccion2 = @p3, TipoLugar = @p4
            WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Contacto = @p2";

        internal const string SQL_LEER = @"
            SELECT TOP 1 Direccion2 FROM dbo.Clientes
            WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Contacto = @p2";

        private static int avisadoSinColumnas;

        /// <summary>Para los tests: lo que devuelve <see cref="Leer"/> sin ir a la BD (null = ir a la BD).</summary>
        internal static Func<string, string, string, string> LectorParaTests { get; set; }

        /// <summary>Guarda lo compuesto por <see cref="Direccion2Cliente"/>. Llamar DESPUÉS de SaveChanges. True si ha escrito.</summary>
        public static bool Guardar(NVEntities db, Cliente cliente, ResultadoDireccion2 direccion2)
        {
            if (db == null || cliente == null || direccion2 == null || !direccion2.Valida || string.IsNullOrWhiteSpace(direccion2.Direccion2)
                || string.IsNullOrWhiteSpace(cliente.Nº_Cliente))
            {
                return false;
            }
            if (!HayColumnas(db, $"no se ha guardado la dirección 2 del cliente {cliente.Nº_Cliente.Trim()}/{cliente.Contacto?.Trim()} («{direccion2.Direccion2}»)"))
            {
                return false;
            }
            try
            {
                int filas = db.Database.ExecuteSqlCommand(SQL_GUARDAR,
                    (cliente.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim(), cliente.Nº_Cliente.Trim(), (cliente.Contacto ?? "0").Trim(),
                    direccion2.Direccion2, direccion2.TipoLugar);
                return filas > 0;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes #874] No se ha podido guardar la dirección 2 del cliente {cliente.Nº_Cliente.Trim()}/{cliente.Contacto?.Trim()} («{direccion2.Direccion2}»): {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>La Dirección 2 del contacto (recortada), o null si no tiene, no existen las columnas o falla la lectura.</summary>
        public static string Leer(NVEntities db, string empresa, string cliente, string contacto)
        {
            if (LectorParaTests != null)
            {
                return LectorParaTests(empresa, cliente, contacto);
            }
            if (db == null || string.IsNullOrWhiteSpace(cliente) || !HayColumnas(db, "la dirección 2 de los clientes no llega a los envíos"))
            {
                return null;
            }
            try
            {
                string valor = db.Database.SqlQuery<string>(SQL_LEER,
                    (empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim(), cliente.Trim(), (contacto ?? "0").Trim()).FirstOrDefault();
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes #874] No se ha podido leer la dirección 2 del cliente {cliente.Trim()}/{contacto?.Trim()}: {ex.Message}", ex));
                return null;
            }
        }

        internal const string SQL_RASTRO_NUMERO_SIN_GOOGLE = @"
            INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@p0, @p1, @p2, @p3)";

        /// <summary>
        /// NestoAPI#874: el usuario confirmó un número de portal que Google no encuentra. Una fila en Modificaciones (Tabla
        /// <see cref="NumeroPropioDireccion.TABLA_MODIFICACIONES"/>; Anterior = cliente/contacto, Nuevo = dirección, CP y número)
        /// para medir cuántos hay y revisarlos si la agencia devuelve el envío. Best-effort: si falla, ELMAH y el alta sigue.
        /// </summary>
        public static bool RegistrarNumeroSinGoogle(NVEntities db, Cliente cliente, string numero, string usuario)
        {
            if (db == null || cliente == null || string.IsNullOrWhiteSpace(cliente.Nº_Cliente))
            {
                return false;
            }
            string quien = $"{cliente.Nº_Cliente.Trim()}/{cliente.Contacto?.Trim()}";
            string que = $"{cliente.Dirección?.Trim()} ({cliente.CodPostal?.Trim()}) · número {NumeroPropioDireccion.Normalizar(numero)} confirmado sin Google";
            try
            {
                return db.Database.ExecuteSqlCommand(SQL_RASTRO_NUMERO_SIN_GOOGLE, NumeroPropioDireccion.TABLA_MODIFICACIONES, quien, que,
                    string.IsNullOrWhiteSpace(usuario) ? UsuarioAuditoriaHelper.DESCONOCIDO : usuario.Trim()) > 0;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes #874] Cliente {quien}: {que}. No se ha podido apuntar en Modificaciones: {ex.Message}", ex));
                return false;
            }
        }

        private static bool HayColumnas(NVEntities db, string consecuencia)
        {
            if (ColumnasOpcionales.Existen(db, TABLA, COLUMNA_DIRECCION2, COLUMNA_TIPO_LUGAR))
            {
                return true;
            }
            if (Interlocked.Exchange(ref avisadoSinColumnas, 1) == 0)
            {
                ElmahHelper.Log(new Exception($"[Clientes #874] Faltan las columnas Clientes.Direccion2 y TipoLugar (Scripts/Issue874_ClientesDireccion2.sql): {consecuencia}. Solo se avisa una vez por proceso."));
            }
            return false;
        }

        internal static void ReiniciarAvisoParaTests() => Interlocked.Exchange(ref avisadoSinColumnas, 0);
    }
}
