using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#716 (2.2): por qué cambia el estado de un cliente. El trigger trgClientesUpd apunta cada cambio en
    /// SeguimientoCambiosEstadoCliente (desde el 16/01/2025) pero sin el porqué; esto lo añade en las columnas Motivo
    /// (varchar(30), catálogo cerrado) y Comentario (nvarchar(200), libre) de esa misma fila, para los modelos de
    /// abandono de la épica #689. Los dos son opcionales: sin ellos la fila queda como hasta ahora.
    /// </summary>
    public static class MotivoCambioEstadoCliente
    {
        // Los que elige la persona en la ficha (Nesto / NestoApp)
        public const string CIERRE = "Cierre";
        public const string TRASPASO = "Traspaso";
        public const string COMPETENCIA = "Competencia";
        public const string IMPAGO = "Impago";
        public const string SIN_ACTIVIDAD = "SinActividad";
        public const string DUPLICADO = "Duplicado";
        public const string CAMBIO_TITULAR = "CambioTitular";
        public const string PETICION_CLIENTE = "PeticionCliente";
        public const string DISTRIBUIDOR = "Distribuidor";
        public const string PUBLICO_FINAL = "PublicoFinal";
        public const string REACTIVACION = "Reactivacion";
        public const string OTRO = "Otro";
        // Los que pone la API cuando el cambio lo hace ella
        public const string DEJAR_DE_VISITAR = "DejarDeVisitar";
        public const string ALTA_CON_NIF = "AltaConNif";

        public const int LONGITUD_MAXIMA = 30;
        public const int LONGITUD_COMENTARIO = 200;

        /// <summary>Código → nombre para mostrar en el desplegable.</summary>
        public static readonly IReadOnlyDictionary<string, string> Nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { CIERRE, "El centro ha cerrado" },
            { TRASPASO, "Traspaso" },
            { COMPETENCIA, "Compra a la competencia" },
            { IMPAGO, "Impago" },
            { SIN_ACTIVIDAD, "Sin actividad" },
            { DUPLICADO, "Ficha duplicada" },
            { CAMBIO_TITULAR, "Cambio de titular" },
            { PETICION_CLIENTE, "Lo pide el cliente" },
            { DISTRIBUIDOR, "Pasa a distribuidor" },
            { PUBLICO_FINAL, "Pasa a público final" },
            { REACTIVACION, "Reactivación" },
            { OTRO, "Otro" },
            { DEJAR_DE_VISITAR, "Dejar de visitar (automático)" },
            { ALTA_CON_NIF, "Alta con NIF (automático)" }
        };

        /// <summary>Los que se ofrecen a la persona, en el orden en que se pintan (los automáticos no).</summary>
        public static readonly IReadOnlyList<string> CodigosManuales = new[]
        {
            CIERRE, TRASPASO, COMPETENCIA, IMPAGO, SIN_ACTIVIDAD, DUPLICADO, CAMBIO_TITULAR, PETICION_CLIENTE, DISTRIBUIDOR, PUBLICO_FINAL, REACTIVACION, OTRO
        };

        public static bool EsValido(string motivo)
        {
            return !string.IsNullOrWhiteSpace(motivo) && Nombres.ContainsKey(motivo.Trim());
        }

        /// <summary>Null si no viene; el código exacto si casa (sin distinguir mayúsculas ni tildes); null si no casa.</summary>
        public static string Normalizar(string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                return null;
            }
            string clave = Rapports.ResultadoRapport.Simplificar(motivo);
            return Nombres.Keys.FirstOrDefault(c => Rapports.ResultadoRapport.Simplificar(c) == clave);
        }

        /// <summary>Null si no hay nada que objetar (también cuando no viene); si no, el motivo para el cliente (400).</summary>
        public static string MotivoParaNoGuardar(string motivo, string comentario)
        {
            if (!string.IsNullOrWhiteSpace(motivo) && Normalizar(motivo) == null)
            {
                return $"El motivo del cambio de estado '{motivo.Trim()}' no está en el catálogo. Valores admitidos: {string.Join(", ", CodigosManuales)}.";
            }
            if (comentario != null && comentario.Trim().Length > LONGITUD_COMENTARIO)
            {
                return $"El comentario del cambio de estado no puede pasar de {LONGITUD_COMENTARIO} caracteres.";
            }
            return null;
        }
    }

    /// <summary>
    /// NestoAPI#716 (2.2): escribe Motivo y Comentario en la fila que el trigger acaba de apuntar en
    /// SeguimientoCambiosEstadoCliente. Best-effort: sin el script (columnas sin crear) no hace nada; con error, el
    /// cambio de estado ya está guardado y el fallo queda en ELMAH.
    /// </summary>
    public static class RegistroMotivoCambioEstado
    {
        internal const string TABLA = "SeguimientoCambiosEstadoCliente";
        internal const string COLUMNA_MOTIVO = "Motivo";
        internal const string COLUMNA_COMENTARIO = "Comentario";
        /// <summary>La fila del trigger es de hace milisegundos; con 10 minutos sobra y no se pisa un cambio viejo.</summary>
        public const int MINUTOS_VENTANA = 10;

        // La última fila del cliente/contacto con ese estado nuevo, sin motivo, de los últimos minutos. Usuario: el trigger
        // pone suser_sname() (la cuenta de la API); si sabemos quién fue, se corrige.
        internal const string SQL_ANOTAR = @"
            UPDATE s SET Motivo = @p4, Comentario = @p5, Usuario = COALESCE(@p6, s.Usuario)
            FROM dbo.SeguimientoCambiosEstadoCliente s
            WHERE s.Id = (SELECT TOP 1 Id FROM dbo.SeguimientoCambiosEstadoCliente
                          WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2 AND EstadoNuevo = @p3
                            AND Motivo IS NULL AND Fecha >= DATEADD(minute, -10, GETDATE())
                          ORDER BY Id DESC)";

        /// <summary>
        /// Anota el motivo del cambio al estado <paramref name="estadoNuevo"/> del cliente. Llamar DESPUÉS de SaveChanges
        /// (la fila la crea el trigger al guardar). Devuelve true si ha tocado una fila.
        /// </summary>
        public static bool Anotar(NVEntities db, string empresa, string cliente, string contacto, short estadoNuevo, string motivo, string comentario, string usuario)
        {
            string codigo = MotivoCambioEstadoCliente.Normalizar(motivo);
            string texto = Recortar(comentario, MotivoCambioEstadoCliente.LONGITUD_COMENTARIO);
            if (db == null || (codigo == null && texto == null) || string.IsNullOrWhiteSpace(cliente))
            {
                return false;
            }
            if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNA_MOTIVO, COLUMNA_COMENTARIO))
            {
                return false;
            }
            try
            {
                int filas = db.Database.ExecuteSqlCommand(SQL_ANOTAR,
                    (empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO).Trim(), cliente.Trim(), (contacto ?? "0").Trim(), estadoNuevo,
                    (object)codigo ?? DBNull.Value, (object)texto ?? DBNull.Value,
                    (object)Recortar(usuario, 100) ?? DBNull.Value);
                return filas > 0;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes #716] No se ha podido anotar el motivo del cambio de estado del cliente {cliente.Trim()}/{contacto?.Trim()}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>Para los que cambian el estado de varias fichas de una vez (DejarDeVisitar): una anotación por cliente.</summary>
        public static int AnotarVarios(NVEntities db, IEnumerable<Cliente> clientes, string motivo, string comentario, string usuario)
        {
            int anotados = 0;
            foreach (Cliente c in clientes ?? Enumerable.Empty<Cliente>())
            {
                if (c != null && Anotar(db, c.Empresa, c.Nº_Cliente, c.Contacto, c.Estado ?? 0, motivo, comentario, usuario))
                {
                    anotados++;
                }
            }
            return anotados;
        }

        internal static string Recortar(string texto, int maximo)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            return limpio.Length > maximo ? limpio.Substring(0, maximo) : limpio;
        }
    }
}
