using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using NestoAPI.Infrastructure;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>NestoAPI#776: un código guardado que no es un código de barras válido (la regla de <see cref="ValidadorGtin"/>).</summary>
    public class CandidatoLimpiezaCodigoBarrasDTO
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Producto activo (Estado &gt;= 0). La limpieza vale para todos; el informe semanal solo cuenta los activos.</summary>
        public bool Activo { get; set; }
        public string Codigo { get; set; }
        /// <summary>Ficha (Productos.CodBarras) o Tabla (ProductosCodigosBarras).</summary>
        public string Donde { get; set; }
        public string Motivo { get; set; }
        /// <summary>La fila de ProductosCodigosBarras (solo los de la tabla). No viaja: es para borrarla.</summary>
        [JsonIgnore]
        public int? Id { get; set; }
    }

    /// <summary>NestoAPI#776: la respuesta de POST api/Productos/CodigosBarras/LimpiarNoValidos.</summary>
    public class ResultadoLimpiezaCodigosBarrasDTO
    {
        public bool Aplicar { get; set; }
        public List<CandidatoLimpiezaCodigoBarrasDTO> Candidatos { get; set; } = new List<CandidatoLimpiezaCodigoBarrasDTO>();
        public int Limpiados { get; set; }
        public List<string> Errores { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#776: un producto (de cualquier empresa y estado) con algo en Productos.CodBarras.</summary>
    public class FichaCodigoBarrasLimpieza
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short Estado { get; set; }
        public string CodBarras { get; set; }
    }

    /// <summary>NestoAPI#776: una fila de ProductosCodigosBarras (activa o no).</summary>
    public class FilaCodigoBarrasLimpieza
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Codigo { get; set; }
        /// <summary>Null si el producto ya no está en Productos.</summary>
        public string Nombre { get; set; }
        public short? Estado { get; set; }
    }

    public interface IRepositorioLimpiezaCodigosBarras
    {
        /// <summary>Todos los productos (todas las empresas, también los no activos) con algo en CodBarras.</summary>
        List<FichaCodigoBarrasLimpieza> LeerFichasConCodigo();
        /// <summary>Todas las filas de ProductosCodigosBarras (también las de baja).</summary>
        List<FilaCodigoBarrasLimpieza> LeerFilasTabla();
        /// <summary>
        /// Limpia el lote en UNA transacción (si algo falla, lanza y no queda nada del lote): la ficha a NULL y la fila de la
        /// tabla borrada, cada una con su fila en Modificaciones. Solo si el código sigue siendo el mismo. Devuelve cuántos ha
        /// limpiado.
        /// </summary>
        int Limpiar(IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario);
    }

    /// <summary>
    /// NestoAPI#776 (hilo de Compras 26-27/10/26): deja en blanco los códigos de barras que no son válidos («0», «1», «123», un
    /// EAN-13 con el control mal…). Mirar primero (aplicar=false) y luego aplicar. La regla es la del informe semanal
    /// (<see cref="ValidadorGtin.MotivoNoValido"/>); los internos del 29 (NestoAPI#762) son EAN-13 válidos y no se tocan.
    /// </summary>
    public class ServicioLimpiezaCodigosBarras
    {
        /// <summary>Productos por transacción.</summary>
        public const int PRODUCTOS_POR_LOTE = 100;

        public const string MENSAJE_SIN_PERMISO = "Para limpiar los códigos de barras no válidos hay que ser de Dirección, Informática o Compras.";

        private static readonly string[] gruposQuePuedenLimpiar =
        {
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA,
            Constantes.GruposSeguridad.COMPRAS
        };

        private readonly IRepositorioLimpiezaCodigosBarras repositorio;

        public ServicioLimpiezaCodigosBarras(IRepositorioLimpiezaCodigosBarras repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public static bool PuedeLimpiar(IPrincipal usuario)
        {
            return usuario != null && gruposQuePuedenLimpiar.Any(g => usuario.IsInRoleSinDominio(g));
        }

        /// <summary>Los candidatos: sin base de datos, es la regla (testeable). Vacíos no cuentan (ya están «sin código»).</summary>
        public static List<CandidatoLimpiezaCodigoBarrasDTO> Candidatos(IEnumerable<FichaCodigoBarrasLimpieza> fichas, IEnumerable<FilaCodigoBarrasLimpieza> filas)
        {
            var candidatos = new List<CandidatoLimpiezaCodigoBarrasDTO>();
            foreach (FichaCodigoBarrasLimpieza ficha in fichas ?? Enumerable.Empty<FichaCodigoBarrasLimpieza>())
            {
                string codigo = ficha?.CodBarras?.Trim();
                if (string.IsNullOrEmpty(codigo) || string.IsNullOrWhiteSpace(ficha.Producto))
                {
                    continue;
                }
                string motivo = ValidadorGtin.MotivoNoValido(codigo);
                if (motivo != null)
                {
                    candidatos.Add(new CandidatoLimpiezaCodigoBarrasDTO
                    {
                        Empresa = ficha.Empresa?.Trim(),
                        Producto = ficha.Producto.Trim(),
                        Nombre = ficha.Nombre?.Trim(),
                        Activo = ficha.Estado >= 0,
                        Codigo = codigo,
                        Donde = InformeCodigosBarras.DONDE_FICHA,
                        Motivo = motivo
                    });
                }
            }
            foreach (FilaCodigoBarrasLimpieza fila in filas ?? Enumerable.Empty<FilaCodigoBarrasLimpieza>())
            {
                // Codigo es NOT NULL en la tabla; una fila en blanco tampoco es un código: se borra igual
                string codigo = fila?.Codigo?.Trim() ?? string.Empty;
                if (fila == null || string.IsNullOrWhiteSpace(fila.Producto))
                {
                    continue;
                }
                string motivo = ValidadorGtin.MotivoNoValido(codigo);
                if (motivo != null)
                {
                    candidatos.Add(new CandidatoLimpiezaCodigoBarrasDTO
                    {
                        Empresa = fila.Empresa?.Trim(),
                        Producto = fila.Producto.Trim(),
                        Nombre = fila.Nombre?.Trim(),
                        Activo = fila.Estado.HasValue && fila.Estado.Value >= 0,
                        Codigo = codigo,
                        Donde = InformeCodigosBarras.DONDE_TABLA,
                        Motivo = motivo,
                        Id = fila.Id
                    });
                }
            }
            // Cada producto con la ficha primero (al ponerla a NULL, el trigger da de baja su copia en la tabla) y luego la tabla
            return candidatos
                .OrderBy(c => c.Empresa, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Donde == InformeCodigosBarras.DONDE_FICHA ? 0 : 1)
                .ThenBy(c => c.Id ?? 0)
                .ToList();
        }

        public ResultadoLimpiezaCodigosBarrasDTO Ejecutar(bool aplicar, string usuario)
        {
            var resultado = new ResultadoLimpiezaCodigosBarrasDTO
            {
                Aplicar = aplicar,
                Candidatos = Candidatos(repositorio.LeerFichasConCodigo(), repositorio.LeerFilasTabla())
            };
            int enFicha = resultado.Candidatos.Count(c => c.Donde == InformeCodigosBarras.DONDE_FICHA);
            int enTabla = resultado.Candidatos.Count - enFicha;
            string resumen = $"{resultado.Candidatos.Count} {(resultado.Candidatos.Count == 1 ? "código no válido" : "códigos no válidos")} " +
                $"({enFicha} en la ficha y {enTabla} en la tabla de códigos)";
            if (!aplicar)
            {
                resultado.Mensaje = $"Modo mirar: no se ha tocado nada. {resumen}. Repita con aplicar=true para dejarlos en blanco.";
                return resultado;
            }
            if (resultado.Candidatos.Count == 0)
            {
                resultado.Mensaje = "No hay ningún código de barras no válido: no hay nada que limpiar.";
                return resultado;
            }

            List<List<CandidatoLimpiezaCodigoBarrasDTO>> porProducto = resultado.Candidatos
                .GroupBy(c => (c.Empresa ?? string.Empty) + "|" + c.Producto, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.ToList())
                .ToList();
            for (int i = 0; i < porProducto.Count; i += PRODUCTOS_POR_LOTE)
            {
                List<List<CandidatoLimpiezaCodigoBarrasDTO>> productosDelLote = porProducto.Skip(i).Take(PRODUCTOS_POR_LOTE).ToList();
                try
                {
                    resultado.Limpiados += repositorio.Limpiar(productosDelLote.SelectMany(p => p).ToList(), usuario);
                }
                catch (Exception)
                {
                    // El lote no ha dejado nada: se repite producto a producto para que uno malo no se lleve a los demás
                    foreach (List<CandidatoLimpiezaCodigoBarrasDTO> producto in productosDelLote)
                    {
                        try
                        {
                            resultado.Limpiados += repositorio.Limpiar(producto, usuario);
                        }
                        catch (Exception ex)
                        {
                            resultado.Errores.Add($"Producto {producto[0].Producto} (empresa {producto[0].Empresa}, " +
                                $"{string.Join(", ", producto.Select(c => $"«{c.Codigo}» en {c.Donde}"))}): {ex.GetBaseException().Message}");
                        }
                    }
                }
            }
            resultado.Mensaje = $"Limpiados {resultado.Limpiados} de {resumen}.";
            if (resultado.Errores.Count > 0)
            {
                resultado.Mensaje += $" {resultado.Errores.Count} {(resultado.Errores.Count == 1 ? "producto no se ha podido limpiar" : "productos no se han podido limpiar")}: vea Errores.";
            }
            else if (resultado.Limpiados < resultado.Candidatos.Count)
            {
                resultado.Mensaje += " Los que faltan habían cambiado mientras tanto y no se han tocado.";
            }
            return resultado;
        }

        /// <summary>La fila de Modificaciones de un código limpiado (Anterior con la clave y el código; Nuevo, vacío).</summary>
        internal static Modificacion Auditoria(CandidatoLimpiezaCodigoBarrasDTO candidato, string usuario)
        {
            bool ficha = candidato.Donde == InformeCodigosBarras.DONDE_FICHA;
            string clave = $"Producto {candidato.Empresa}/{candidato.Producto}" + (ficha ? string.Empty : $" Id={candidato.Id}");
            return new Modificacion
            {
                Tabla = ficha ? TABLA_FICHA : TABLA_CODIGOS,
                Anterior = $"{clave} {(ficha ? "CodBarras" : "Codigo")}={candidato.Codigo}",
                Nuevo = $"{clave} {(ficha ? "CodBarras=" : "(fila borrada)")} (NestoAPI#776 código no válido: {candidato.Motivo}; usuario {usuario})",
                Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario)
            };
        }

        internal const string TABLA_FICHA = "Productos";
        internal const string TABLA_CODIGOS = "ProductosCodigosBarras";
    }

    /// <summary>NestoAPI#776: ADO.NET, como el informe semanal (RepositorioInformeCodigosBarrasSql).</summary>
    public class RepositorioLimpiezaCodigosBarrasSql : IRepositorioLimpiezaCodigosBarras
    {
        private const int TIMEOUT_SEGUNDOS = 120;

        internal const string SQL_FICHAS = @"
SELECT RTRIM(p.Empresa), RTRIM(p.[Número]), RTRIM(p.Nombre), ISNULL(p.Estado, 0), RTRIM(p.CodBarras)
FROM Productos p WITH (NOLOCK)
WHERE p.CodBarras IS NOT NULL AND RTRIM(p.CodBarras) <> ''";

        internal const string SQL_FILAS_TABLA = @"
SELECT c.Id, RTRIM(c.Empresa), RTRIM(c.Producto), RTRIM(c.Codigo), RTRIM(p.Nombre), p.Estado
FROM ProductosCodigosBarras c WITH (NOLOCK)
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = c.Empresa AND p.[Número] = c.Producto";

        // En blanco = NULL, como los 33.000 productos sin código y como da de alta la API (ProductoCursoEvento). El trigger
        // trgProductosCodigosBarras da de baja su copia (Origen Ficha) en ProductosCodigosBarras, que se borra después.
        internal const string SQL_LIMPIAR_FICHA = @"
UPDATE Productos SET CodBarras = NULL, Usuario = @usuario, [Fecha Modificación] = GETDATE()
WHERE Empresa = @empresa AND [Número] = @producto AND RTRIM(CodBarras) = @codigo";

        internal const string SQL_BORRAR_FILA = @"
DELETE FROM ProductosCodigosBarras WHERE Id = @id AND RTRIM(Codigo) = @codigo";

        // Usuario explícito (EF no lo manda: en el EDMX es Computed y saldría la cuenta de la máquina)
        internal const string SQL_AUDITORIA = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha) VALUES (@tabla, @anterior, @nuevo, @usuario, GETDATE())";

        private readonly string cadenaConexion;

        public RepositorioLimpiezaCodigosBarrasSql(string cadenaConexion)
        {
            this.cadenaConexion = string.IsNullOrWhiteSpace(cadenaConexion) ? throw new ArgumentNullException(nameof(cadenaConexion)) : cadenaConexion;
        }

        public static RepositorioLimpiezaCodigosBarrasSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"]
                ?? throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            return new RepositorioLimpiezaCodigosBarrasSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = "NestoAPI-LimpiezaCodigosBarras"
            }.ConnectionString);
        }

        public List<FichaCodigoBarrasLimpieza> LeerFichasConCodigo()
        {
            var fichas = new List<FichaCodigoBarrasLimpieza>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_FICHAS, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        fichas.Add(new FichaCodigoBarrasLimpieza
                        {
                            Empresa = Cadena(lector, 0),
                            Producto = Cadena(lector, 1),
                            Nombre = Cadena(lector, 2),
                            Estado = Convert.ToInt16(lector.GetValue(3), CultureInfo.InvariantCulture),
                            CodBarras = Cadena(lector, 4)
                        });
                    }
                }
            }
            return fichas;
        }

        public List<FilaCodigoBarrasLimpieza> LeerFilasTabla()
        {
            var filas = new List<FilaCodigoBarrasLimpieza>();
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_FILAS_TABLA, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS })
            {
                conexion.Open();
                using (SqlDataReader lector = comando.ExecuteReader())
                {
                    while (lector.Read())
                    {
                        filas.Add(new FilaCodigoBarrasLimpieza
                        {
                            Id = lector.GetInt32(0),
                            Empresa = Cadena(lector, 1),
                            Producto = Cadena(lector, 2),
                            Codigo = Cadena(lector, 3),
                            Nombre = Cadena(lector, 4),
                            Estado = lector.IsDBNull(5) ? (short?)null : Convert.ToInt16(lector.GetValue(5), CultureInfo.InvariantCulture)
                        });
                    }
                }
            }
            return filas;
        }

        public int Limpiar(IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario)
        {
            if (lote == null || lote.Count == 0)
            {
                return 0;
            }
            string usuarioFicha = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            int limpiados = 0;
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                conexion.Open();
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    try
                    {
                        foreach (CandidatoLimpiezaCodigoBarrasDTO candidato in lote)
                        {
                            bool ficha = candidato.Donde == InformeCodigosBarras.DONDE_FICHA;
                            using (var comando = new SqlCommand(ficha ? SQL_LIMPIAR_FICHA : SQL_BORRAR_FILA, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                            {
                                comando.Parameters.Add("@codigo", SqlDbType.VarChar, 20).Value = candidato.Codigo ?? string.Empty;
                                if (ficha)
                                {
                                    comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = candidato.Empresa;
                                    comando.Parameters.Add("@producto", SqlDbType.Char, 15).Value = candidato.Producto;
                                    comando.Parameters.Add("@usuario", SqlDbType.Char, 30).Value = usuarioFicha;
                                }
                                else
                                {
                                    comando.Parameters.Add("@id", SqlDbType.Int).Value = candidato.Id ?? 0;
                                }
                                if (comando.ExecuteNonQuery() == 0)
                                {
                                    continue; // ha cambiado mientras tanto: no se toca ni se audita
                                }
                            }
                            Modificacion auditoria = ServicioLimpiezaCodigosBarras.Auditoria(candidato, usuario);
                            using (var comando = new SqlCommand(SQL_AUDITORIA, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                            {
                                comando.Parameters.Add("@tabla", SqlDbType.NVarChar, 50).Value = auditoria.Tabla;
                                comando.Parameters.Add("@anterior", SqlDbType.NVarChar, -1).Value = auditoria.Anterior;
                                comando.Parameters.Add("@nuevo", SqlDbType.NVarChar, -1).Value = auditoria.Nuevo;
                                comando.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value = auditoria.Usuario;
                                _ = comando.ExecuteNonQuery();
                            }
                            limpiados++;
                        }
                        transaccion.Commit();
                    }
                    catch
                    {
                        try
                        {
                            transaccion.Rollback();
                        }
                        catch (Exception)
                        {
                            // la conexión ya la ha deshecho; lo que importa es el error original
                        }
                        throw;
                    }
                }
            }
            return limpiados;
        }

        private static string Cadena(SqlDataReader lector, int indice) => lector.IsDBNull(indice) ? null : lector.GetString(indice);
    }
}
