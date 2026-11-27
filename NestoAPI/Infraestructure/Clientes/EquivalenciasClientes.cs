using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>Cuerpo de POST api/Clientes/Equivalentes.</summary>
    public class PeticionEnlaceEquivalenteDTO
    {
        public string Empresa { get; set; }
        /// <summary>La ficha que queda viva (la que apunta).</summary>
        public string Cliente { get; set; }
        /// <summary>La otra ficha (la duplicada, o la que tenía antes del cambio de forma fiscal).</summary>
        public string ClienteAnterior { get; set; }
        /// <summary>«Duplicado» o «CambioFiscal».</summary>
        public string Motivo { get; set; }
        public string Comentario { get; set; }
    }

    /// <summary>Una ficha de un grupo de clientes equivalentes, con el enlace a SU anterior (si lo tiene).</summary>
    public class FichaEquivalenteDTO
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        /// <summary>Estado de la ficha principal.</summary>
        public short? Estado { get; set; }
        /// <summary>Algún contacto de la ficha está de alta (Estado &gt;= 0).</summary>
        public bool Activa { get; set; }
        public string Nif { get; set; }
        /// <summary>Es la ficha consultada.</summary>
        public bool EsLaConsultada { get; set; }
        /// <summary>A qué ficha apunta esta como su anterior (null = a ninguna).</summary>
        public string ClienteAnterior { get; set; }
        /// <summary>Motivo del enlace a su anterior («Duplicado», «CambioFiscal»; null si no consta).</summary>
        public string Motivo { get; set; }
        public string Comentario { get; set; }
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
    }

    /// <summary>Respuesta de GET api/Clientes/{empresa}/{cliente}/Equivalentes.</summary>
    public class ClientesEquivalentesDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>El cliente consultado primero; después, el resto del grupo desde la ficha más reciente.</summary>
        public List<FichaEquivalenteDTO> Fichas { get; set; } = new List<FichaEquivalenteDTO>();
    }

    /// <summary>Respuesta de POST api/Clientes/Equivalentes y de DELETE …/Equivalentes/{clienteAnterior}.</summary>
    public class ResultadoEnlaceEquivalenteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string ClienteAnterior { get; set; }
        public string Motivo { get; set; }
        /// <summary>Se ha escrito (creado o deshecho) un enlace con esta llamada.</summary>
        public bool Cambiado { get; set; }
        /// <summary>Ya eran equivalentes: no se ha escrito nada.</summary>
        public bool YaEstaba { get; set; }
        /// <summary>El enlace escrito, que con la cadena (A → B ya existía y se pide A → C) es B → C.</summary>
        public string EnlaceCliente { get; set; }
        public string EnlaceClienteAnterior { get; set; }
        public bool Encadenado { get; set; }
        public string Mensaje { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>El grupo tal como queda.</summary>
        public List<FichaEquivalenteDTO> Fichas { get; set; } = new List<FichaEquivalenteDTO>();
    }

    /// <summary>Lo que hace falta de una ficha de cliente (la principal) para enlazarla.</summary>
    public class DatosFichaEquivalente
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public short? Estado { get; set; }
        public bool Activa { get; set; }
        public string Nif { get; set; }
        public string Vendedor { get; set; }
    }

    /// <summary>Una fila de dbo.ClientesAnteriores con el motivo y el comentario (null sin Scripts/Issue876_ClientesAnteriores_Motivo.sql).</summary>
    public class EnlaceEquivalenteCliente
    {
        public string Cliente { get; set; }
        public string ClienteAnterior { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public string Motivo { get; set; }
        public string Comentario { get; set; }
    }

    public class ResultadoEquivalencias<T>
    {
        public int Estado { get; set; } = 200;
        public string Motivo { get; set; }
        /// <summary>Con el 409: CICLO (enlazarlos haría un ciclo) u OCUPADO (otro ha tocado el enlace a la vez).</summary>
        public string Codigo { get; set; }
        public T Datos { get; set; }

        public static ResultadoEquivalencias<T> Error(int estado, string motivo, string codigo = null)
            => new ResultadoEquivalencias<T> { Estado = estado, Motivo = motivo, Codigo = codigo };
        public static ResultadoEquivalencias<T> Ok(T datos) => new ResultadoEquivalencias<T> { Datos = datos };
    }

    /// <summary>Lo que decide <see cref="ReglaEnlaceEquivalentes.Decidir"/>.</summary>
    public class DecisionEnlaceEquivalente
    {
        public int Estado { get; set; } = 200;
        public string Motivo { get; set; }
        public string Codigo { get; set; }
        public bool YaEstaba { get; set; }
        /// <summary>El motivo normalizado («Duplicado» o «CambioFiscal»).</summary>
        public string MotivoEnlace { get; set; }
        /// <summary>La fila que hay que escribir: <see cref="Cliente"/> → <see cref="ClienteAnterior"/>.</summary>
        public string Cliente { get; set; }
        public string ClienteAnterior { get; set; }
        public bool Encadenado { get; set; }
        public string Mensaje { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();

        internal static DecisionEnlaceEquivalente Error(int estado, string motivo, string codigo = null)
            => new DecisionEnlaceEquivalente { Estado = estado, Motivo = motivo, Codigo = codigo };
    }

    /// <summary>
    /// Carlos 26-27/11/26 (NestoAPI#876): enlazar dos fichas de cliente que son la misma persona (DUPLICADO: dada de alta dos veces, mismo
    /// NIF) o el mismo cliente con otra forma fiscal (CAMBIO FISCAL), sin script de sa. La misma dbo.ClientesAnteriores: la ficha que
    /// queda apunta a la otra como su «anterior». LA REGLA (pura).
    /// </summary>
    public static class ReglaEnlaceEquivalentes
    {
        public const string DUPLICADO = "Duplicado";
        public const string CAMBIO_FISCAL = "CambioFiscal";
        public const string CODIGO_CICLO = "CICLO";
        public const string CODIGO_OCUPADO = "OCUPADO";
        public const int LARGO_COMENTARIO = 255;

        /// <summary>«Duplicado» o «CambioFiscal» (sin distinguir mayúsculas, espacios, guiones ni tildes de más); null si no es ninguno.</summary>
        public static string NormalizarMotivo(string motivo)
        {
            string limpio = new string((motivo ?? string.Empty).Where(char.IsLetter).ToArray()).ToUpperInvariant();
            switch (limpio)
            {
                case "DUPLICADO":
                case "DUPLICADA":
                    return DUPLICADO;
                case "CAMBIOFISCAL":
                case "CAMBIODEFORMAFISCAL":
                    return CAMBIO_FISCAL;
                default:
                    return null;
            }
        }

        public static string ComentarioNormalizado(string comentario)
        {
            string c = comentario?.Trim();
            return string.IsNullOrEmpty(c) ? null : c.Length <= LARGO_COMENTARIO ? c : c.Substring(0, LARGO_COMENTARIO);
        }

        /// <summary>
        /// Qué hacer al pedir enlazar <paramref name="cliente"/> (la que queda) con <paramref name="anterior"/> (la otra).
        /// <list type="bullet">
        /// <item>400: falta alguno, son el mismo o el motivo no es «Duplicado» ni «CambioFiscal». 404: alguna ficha no existe.</item>
        /// <item>200 YaEstaba: la otra ya está en la cadena del cliente (directa o encadenada): no se escribe nada.</item>
        /// <item>409 CICLO: la cadena de la otra llega a alguna ficha de la cadena del cliente (al revés, o con una ficha en común).</item>
        /// <item>Si el cliente ya tiene anterior, se ENCADENA como el script del 26/11/26: el enlace va de la última ficha de su
        /// cadena a la otra (A → B y se pide A → C: B → C).</item>
        /// <item>Avisos, sin impedir: con «Duplicado», NIF distintos o la otra ficha aún de alta; con «CambioFiscal», el mismo NIF;
        /// y si la otra ya era el anterior de otra ficha (queda una rama).</item>
        /// </list>
        /// </summary>
        public static DecisionEnlaceEquivalente Decidir(string cliente, string anterior, string motivo, DatosFichaEquivalente fichaCliente,
            DatosFichaEquivalente fichaAnterior, IReadOnlyDictionary<string, string> anteriorDe)
        {
            string c = cliente?.Trim();
            string a = anterior?.Trim();
            if (string.IsNullOrEmpty(c) || string.IsNullOrEmpty(a))
            {
                return DecisionEnlaceEquivalente.Error(400, "Indique la ficha que queda (Cliente) y la otra ficha (ClienteAnterior).");
            }
            if (string.Equals(c, a, StringComparison.OrdinalIgnoreCase))
            {
                return DecisionEnlaceEquivalente.Error(400, $"Las dos fichas son la misma ({c}): indique dos clientes distintos.");
            }
            string motivoEnlace = NormalizarMotivo(motivo);
            if (motivoEnlace == null)
            {
                return DecisionEnlaceEquivalente.Error(400, $"Indique el motivo del enlace: «{DUPLICADO}» (la misma persona dada de alta dos veces) " +
                    $"o «{CAMBIO_FISCAL}» (el mismo cliente con otra forma fiscal).");
            }
            if (fichaCliente == null)
            {
                return DecisionEnlaceEquivalente.Error(404, $"No existe el cliente {c}.");
            }
            if (fichaAnterior == null)
            {
                return DecisionEnlaceEquivalente.Error(404, $"No existe el cliente {a}.");
            }
            anteriorDe = anteriorDe ?? new Dictionary<string, string>();
            IReadOnlyList<string> cadenaCliente = ReglaClientesEquivalentes.Cadena(c, anteriorDe);
            if (cadenaCliente.Contains(a, StringComparer.OrdinalIgnoreCase))
            {
                bool directo = cadenaCliente.Count > 1 && string.Equals(cadenaCliente[1], a, StringComparison.OrdinalIgnoreCase);
                return new DecisionEnlaceEquivalente
                {
                    YaEstaba = true,
                    MotivoEnlace = motivoEnlace,
                    Mensaje = directo
                        ? $"El {c} ya tenía como anterior al {a}: no se cambia nada."
                        : $"El {c} y el {a} ya son equivalentes ({string.Join(" → ", cadenaCliente)}): no se cambia nada."
                };
            }
            IReadOnlyList<string> cadenaAnterior = ReglaClientesEquivalentes.Cadena(a, anteriorDe);
            string comun = cadenaAnterior.FirstOrDefault(f => cadenaCliente.Contains(f, StringComparer.OrdinalIgnoreCase));
            if (comun != null)
            {
                return DecisionEnlaceEquivalente.Error(409, string.Equals(comun, c, StringComparison.OrdinalIgnoreCase)
                    ? $"El {c} ya figura como anterior del {a} ({string.Join(" → ", cadenaAnterior)}): enlazarlos al revés haría un ciclo. " +
                      "Si la ficha que queda es la otra, deshaga antes ese enlace."
                    : $"El {a} y el {c} ya llegan los dos al {comun} ({string.Join(" → ", cadenaAnterior)} y {string.Join(" → ", cadenaCliente)}): " +
                      "enlazarlos haría un ciclo. Deshaga antes uno de los enlaces.", CODIGO_CICLO);
            }
            string ultima = cadenaCliente[cadenaCliente.Count - 1];
            if (anteriorDe.ContainsKey(ultima))
            {
                // La cadena se ha cortado por un ciclo en los datos o por el tope de saltos: no se añade nada encima
                return DecisionEnlaceEquivalente.Error(409, $"La cadena de fichas del {c} ({string.Join(" → ", cadenaCliente)}) no termina: " +
                    "revise los enlaces antes de añadir otro.", CODIGO_CICLO);
            }
            var decision = new DecisionEnlaceEquivalente
            {
                MotivoEnlace = motivoEnlace,
                Cliente = ultima,
                ClienteAnterior = a,
                Encadenado = !string.Equals(ultima, c, StringComparison.OrdinalIgnoreCase)
            };
            decision.Mensaje = decision.Encadenado
                ? $"El {c} ya tenía anterior ({string.Join(" → ", cadenaCliente)}): se encadena, {ultima} → {a}."
                : $"Enlazados: el {c} tiene como anterior al {a} ({motivoEnlace}).";
            if (motivoEnlace == DUPLICADO)
            {
                string nifC = NormalizarNif(fichaCliente.Nif);
                string nifA = NormalizarNif(fichaAnterior.Nif);
                if (nifC != nifA)
                {
                    decision.Avisos.Add($"Los NIF no coinciden: el {c} tiene {nifC ?? "(sin NIF)"} y el {a} tiene {nifA ?? "(sin NIF)"}. " +
                        "Compruebe que son de verdad la misma persona.");
                }
                if (fichaAnterior.Activa)
                {
                    decision.Avisos.Add($"El {a} sigue de alta: si es un duplicado, déjelo de baja con el motivo «Duplicado».");
                }
            }
            else
            {
                string aviso = ReglaClienteAnterior.AvisoMismoNif(new DatosClienteAnterior { Cliente = a, Nif = fichaAnterior.Nif }, fichaCliente.Nif);
                if (aviso != null)
                {
                    decision.Avisos.Add(aviso);
                }
            }
            List<string> otros = anteriorDe.Where(e => string.Equals(e.Value?.Trim(), a, StringComparison.OrdinalIgnoreCase)
                    && !cadenaCliente.Contains(e.Key.Trim(), StringComparer.OrdinalIgnoreCase))
                .Select(e => e.Key.Trim()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            if (otros.Any())
            {
                decision.Avisos.Add($"El {a} ya era el anterior del {string.Join(", ", otros)}: las fichas quedan todas en el mismo grupo de equivalentes.");
            }
            return decision;
        }

        /// <summary>Deshacer: solo si la fila es exactamente <paramref name="cliente"/> → <paramref name="anterior"/>. Null si vale; si no, el motivo del 404.</summary>
        public static string MotivoNoSePuedeDeshacer(string cliente, string anterior, IReadOnlyDictionary<string, string> anteriorDe)
        {
            string c = cliente?.Trim();
            string a = anterior?.Trim();
            if (string.IsNullOrEmpty(c) || string.IsNullOrEmpty(a))
            {
                return "Indique el cliente y su anterior.";
            }
            return anteriorDe != null && anteriorDe.TryGetValue(c, out string suyo) && string.Equals(suyo?.Trim(), a, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"El {c} no tiene como anterior al {a}: no hay nada que deshacer.";
        }

        /// <summary>Las fichas del grupo, en el orden de <see cref="ReglaClientesEquivalentes.Grupo"/>, con su enlace (si lo tienen).</summary>
        public static List<FichaEquivalenteDTO> Fichas(string consultado, IReadOnlyList<string> grupo, IEnumerable<DatosFichaEquivalente> fichas,
            IEnumerable<EnlaceEquivalenteCliente> enlaces)
        {
            Dictionary<string, DatosFichaEquivalente> porCliente = (fichas ?? Enumerable.Empty<DatosFichaEquivalente>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Cliente))
                .GroupBy(f => f.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, EnlaceEquivalenteCliente> enlacePorCliente = (enlaces ?? Enumerable.Empty<EnlaceEquivalenteCliente>())
                .Where(e => !string.IsNullOrWhiteSpace(e?.Cliente))
                .GroupBy(e => e.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Fecha).First(), StringComparer.OrdinalIgnoreCase);
            return (grupo ?? new List<string>()).Select(cliente =>
            {
                _ = porCliente.TryGetValue(cliente, out DatosFichaEquivalente ficha);
                _ = enlacePorCliente.TryGetValue(cliente, out EnlaceEquivalenteCliente enlace);
                return new FichaEquivalenteDTO
                {
                    Cliente = cliente,
                    Nombre = ficha?.Nombre?.Trim(),
                    Estado = ficha?.Estado,
                    Activa = ficha?.Activa ?? false,
                    Nif = ficha?.Nif?.Trim(),
                    EsLaConsultada = string.Equals(cliente, consultado?.Trim(), StringComparison.OrdinalIgnoreCase),
                    ClienteAnterior = enlace?.ClienteAnterior?.Trim(),
                    Motivo = enlace == null ? null : NormalizarMotivo(enlace.Motivo) ?? enlace.Motivo?.Trim(),
                    Comentario = enlace?.Comentario?.Trim(),
                    Usuario = enlace?.Usuario?.Trim(),
                    Fecha = enlace?.Fecha
                };
            }).ToList();
        }

        /// <summary>Lo que se apunta en Modificaciones (Tabla «ClientesAnteriores», con el mismo formato que el alta: «cliente: anterior»).</summary>
        public static string Rastro(string cliente, string anterior, string motivo, string comentario)
            => $"{cliente?.Trim()}: {anterior?.Trim()} ({motivo}{(string.IsNullOrEmpty(comentario) ? string.Empty : "; " + comentario)})";

        private static string NormalizarNif(string nif)
        {
            string limpio = new string((nif ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return limpio.Length == 0 ? null : limpio;
        }
    }

    /// <summary>
    /// Carlos 27/11/26: qué fichas mira una consulta de SOLO LECTURA (extracto, rapports) con <c>incluirEquivalentes</c>. Sin pedirlo,
    /// exactamente el cliente pedido (nada cambia para Nesto, NestoApp ni TiendasNuevaVision). Nunca lanza: si no se pueden leer los
    /// equivalentes, solo el cliente.
    /// </summary>
    public static class ConsultaConEquivalentes
    {
        public static IReadOnlyList<string> Clientes(IClientesEquivalentes equivalentes, string empresa, string cliente, bool incluirEquivalentes)
        {
            string c = cliente?.Trim();
            if (!incluirEquivalentes || equivalentes == null || string.IsNullOrEmpty(c))
            {
                return new List<string> { cliente };
            }
            try
            {
                // ClientesEquivalentes va con ConfigureAwait(false) de punta a punta: esperar aquí no se bloquea con el contexto de ASP.NET
                IReadOnlyList<string> todas = equivalentes.Todas(empresa, c).GetAwaiter().GetResult();
                return todas != null && todas.Count > 0 ? todas : new List<string> { c };
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes equivalentes] No se han podido leer los equivalentes del {c}: {ex.Message}", ex));
                return new List<string> { c };
            }
        }

        /// <summary>Las demás fichas (sin el cliente pedido).</summary>
        public static List<string> Otras(IReadOnlyList<string> clientes, string cliente)
            => (clientes ?? new List<string>()).Where(x => !string.Equals(x?.Trim(), cliente?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>dbo.ClientesAnteriores para enlazar y deshacer a mano (POST/DELETE api/Clientes/Equivalentes) y su consulta.</summary>
    public interface IRepositorioEquivalenciasClientes
    {
        bool TablaExiste();
        Task<List<EnlaceEquivalenteCliente>> LeerEnlaces(string empresa);
        /// <summary>De cada cliente, su ficha principal (los que no existen no salen).</summary>
        Task<List<DatosFichaEquivalente>> LeerFichas(string empresa, IEnumerable<string> clientes);
        /// <summary>En UNA transacción: la fila (solo si el cliente aún no tiene anterior) y la de Modificaciones. False si ya lo tenía.</summary>
        Task<bool> Enlazar(string empresa, string cliente, string anterior, string motivo, string comentario, string usuario, string rastro);
        /// <summary>En UNA transacción: borra la fila (solo si es cliente → anterior) y la de Modificaciones. False si no estaba.</summary>
        Task<bool> Deshacer(string empresa, string cliente, string anterior, string usuario, string rastro);
    }

    /// <summary>ADO sobre NestoConnection. Motivo y Comentario solo si están (Scripts/Issue876_ClientesAnteriores_Motivo.sql); sin ellos, todo igual.</summary>
    public class RepositorioEquivalenciasClientesSql : IRepositorioEquivalenciasClientes
    {
        private static readonly ConcurrentDictionary<string, bool> existe = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly string cadenaConexion;

        public RepositorioEquivalenciasClientesSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        internal const string SQL_EXISTE_TABLA = "SELECT CASE WHEN OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL THEN 1 ELSE 0 END";
        internal const string SQL_EXISTEN_COLUMNAS = "SELECT CASE WHEN COL_LENGTH(N'dbo.ClientesAnteriores', N'Motivo') IS NOT NULL " +
            "AND COL_LENGTH(N'dbo.ClientesAnteriores', N'Comentario') IS NOT NULL THEN 1 ELSE 0 END";

        internal const string SQL_ENLACES = "SELECT RTRIM(Cliente), RTRIM(ClienteAnterior), Fecha, RTRIM(Usuario){0} FROM dbo.ClientesAnteriores WITH (NOLOCK) WHERE Empresa = @empresa";
        internal const string COLUMNAS_MOTIVO = ", RTRIM(Motivo), Comentario";

        internal const string SQL_FICHAS = @"
SELECT RTRIM(c.[Nº Cliente]), RTRIM(c.Nombre), c.Estado, RTRIM(c.[CIF/NIF]), RTRIM(c.Vendedor),
       CASE WHEN EXISTS (SELECT 1 FROM Clientes v WHERE v.Empresa = c.Empresa AND v.[Nº Cliente] = c.[Nº Cliente] AND v.Estado >= 0) THEN 1 ELSE 0 END
FROM Clientes c
WHERE c.Empresa = @empresa AND c.[Nº Cliente] IN ({0})
ORDER BY c.[Nº Cliente], c.ClientePrincipal DESC, c.Contacto";

        internal const string SQL_ENLAZAR = @"
IF EXISTS (SELECT 1 FROM dbo.ClientesAnteriores WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente)
    SELECT 0;
ELSE
BEGIN
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario{0}) VALUES (@empresa, @cliente, @anterior, GETDATE(), @usuario{1});
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES (N'" + RepositorioClientesAnterioresSql.TABLA + @"', CONCAT(RTRIM(@cliente), N': sin anterior'), @rastro, @usuario, GETDATE());
    SELECT 1;
END";

        internal const string SQL_DESHACER = @"
DELETE FROM dbo.ClientesAnteriores WHERE Empresa = @empresa AND Cliente = @cliente AND ClienteAnterior = @anterior;
IF @@ROWCOUNT = 1
BEGIN
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES (N'" + RepositorioClientesAnterioresSql.TABLA + @"', @rastro, CONCAT(RTRIM(@cliente), N': sin anterior (enlace deshecho)'), @usuario, GETDATE());
    SELECT 1;
END
ELSE
    SELECT 0;";

        public bool TablaExiste() => Existe("TABLA", SQL_EXISTE_TABLA);

        private bool HayMotivo() => Existe("MOTIVO", SQL_EXISTEN_COLUMNAS);

        /// <summary>Como <see cref="ColumnasOpcionales"/>: solo se recuerda el «sí»; ante un error, false.</summary>
        private bool Existe(string clave, string sql)
        {
            if (existe.TryGetValue(clave, out bool conocido))
            {
                return conocido;
            }
            try
            {
                using (var conexion = new SqlConnection(cadenaConexion))
                using (var comando = new SqlCommand(sql, conexion))
                {
                    conexion.Open();
                    bool si = Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
                    if (si)
                    {
                        existe[clave] = true;
                    }
                    return si;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<EnlaceEquivalenteCliente>> LeerEnlaces(string empresa)
        {
            bool conMotivo = HayMotivo();
            var lista = new List<EnlaceEquivalenteCliente>();
            await Leer(string.Format(CultureInfo.InvariantCulture, SQL_ENLACES, conMotivo ? COLUMNAS_MOTIVO : string.Empty),
                c => Empresa(c, empresa), r => lista.Add(new EnlaceEquivalenteCliente
                {
                    Cliente = Texto(r, 0),
                    ClienteAnterior = Texto(r, 1),
                    Fecha = r.GetDateTime(2),
                    Usuario = Texto(r, 3),
                    Motivo = conMotivo ? Texto(r, 4) : null,
                    Comentario = conMotivo ? Texto(r, 5) : null
                })).ConfigureAwait(false);
            return lista;
        }

        public async Task<List<DatosFichaEquivalente>> LeerFichas(string empresa, IEnumerable<string> clientes)
        {
            List<string> numeros = (clientes ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var lista = new List<DatosFichaEquivalente>();
            if (numeros.Count == 0)
            {
                return lista;
            }
            string sql = string.Format(CultureInfo.InvariantCulture, SQL_FICHAS, string.Join(", ", numeros.Select((n, i) => "@c" + i)));
            await Leer(sql, c =>
            {
                Empresa(c, empresa);
                for (int i = 0; i < numeros.Count; i++)
                {
                    _ = c.Parameters.Add("@c" + i, SqlDbType.Char, 10).Value = numeros[i];
                }
            }, r =>
            {
                string cliente = Texto(r, 0);
                if (lista.Any(f => string.Equals(f.Cliente, cliente, StringComparison.OrdinalIgnoreCase)))
                {
                    return; // la primera de cada cliente es la principal
                }
                lista.Add(new DatosFichaEquivalente
                {
                    Cliente = cliente,
                    Nombre = Texto(r, 1),
                    Estado = r.IsDBNull(2) ? (short?)null : Convert.ToInt16(r.GetValue(2), CultureInfo.InvariantCulture),
                    Nif = Texto(r, 3),
                    Vendedor = Texto(r, 4),
                    Activa = Convert.ToInt32(r.GetValue(5), CultureInfo.InvariantCulture) == 1
                });
            }).ConfigureAwait(false);
            return lista;
        }

        public async Task<bool> Enlazar(string empresa, string cliente, string anterior, string motivo, string comentario, string usuario, string rastro)
        {
            bool conMotivo = HayMotivo();
            string sql = string.Format(CultureInfo.InvariantCulture, SQL_ENLAZAR, conMotivo ? ", Motivo, Comentario" : string.Empty,
                conMotivo ? ", @motivo, @comentario" : string.Empty);
            object hecho = await EscalarEnTransaccion(sql, c =>
            {
                Parametros(c, empresa, cliente, anterior, usuario, rastro);
                if (conMotivo)
                {
                    _ = c.Parameters.Add("@motivo", SqlDbType.VarChar, 20).Value = (object)motivo ?? DBNull.Value;
                    _ = c.Parameters.Add("@comentario", SqlDbType.NVarChar, ReglaEnlaceEquivalentes.LARGO_COMENTARIO).Value = (object)comentario ?? DBNull.Value;
                }
            }).ConfigureAwait(false);
            ClientesEquivalentes.Invalidar();
            return Convert.ToInt32(hecho, CultureInfo.InvariantCulture) == 1;
        }

        public async Task<bool> Deshacer(string empresa, string cliente, string anterior, string usuario, string rastro)
        {
            object hecho = await EscalarEnTransaccion(SQL_DESHACER, c => Parametros(c, empresa, cliente, anterior, usuario, rastro)).ConfigureAwait(false);
            ClientesEquivalentes.Invalidar();
            return Convert.ToInt32(hecho, CultureInfo.InvariantCulture) == 1;
        }

        private static void Parametros(SqlCommand c, string empresa, string cliente, string anterior, string usuario, string rastro)
        {
            Empresa(c, empresa);
            _ = c.Parameters.Add("@cliente", SqlDbType.Char, 10).Value = cliente?.Trim() ?? string.Empty;
            _ = c.Parameters.Add("@anterior", SqlDbType.Char, 10).Value = anterior?.Trim() ?? string.Empty;
            _ = c.Parameters.Add("@usuario", SqlDbType.VarChar, 30).Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            _ = c.Parameters.Add("@rastro", SqlDbType.NVarChar, -1).Value = rastro ?? string.Empty;
        }

        private static void Empresa(SqlCommand c, string empresa)
            => _ = c.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa?.Trim() ?? string.Empty;

        private async Task Leer(string sql, Action<SqlCommand> parametros, Action<SqlDataReader> fila)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(sql, conexion))
                {
                    parametros(comando);
                    using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await r.ReadAsync().ConfigureAwait(false))
                        {
                            fila(r);
                        }
                    }
                }
            }
        }

        private async Task<object> EscalarEnTransaccion(string sql, Action<SqlCommand> parametros)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                using (var comando = new SqlCommand(sql, conexion, transaccion))
                {
                    parametros(comando);
                    object resultado = await comando.ExecuteScalarAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
            }
        }

        private static string Texto(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
    }

    /// <summary>
    /// Carlos 27/11/26: enlazar a mano dos fichas equivalentes (duplicado o cambio de forma fiscal), deshacerlo y ver el grupo, sin
    /// script de sa. LA REGLA: <see cref="ReglaEnlaceEquivalentes"/>. Permisos (los de #876): Administración, Dirección, Informática y
    /// el vendedor de la otra ficha o su jefe de equipo, para escribir; leer, cualquier usuario de dentro.
    /// </summary>
    public class ServicioEquivalenciasClientes
    {
        public const string MENSAJE_SIN_TABLA = "Falta la tabla dbo.ClientesAnteriores (Scripts/Issue876_ClientesAnteriores.sql): " +
            "no se pueden enlazar fichas de cliente todavía.";

        private readonly IRepositorioEquivalenciasClientes repositorio;

        public ServicioEquivalenciasClientes(IRepositorioEquivalenciasClientes repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ResultadoEquivalencias<ClientesEquivalentesDTO>> Leer(string empresa, string cliente)
        {
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            if (string.IsNullOrEmpty(cli))
            {
                return ResultadoEquivalencias<ClientesEquivalentesDTO>.Error(400, "Indique el cliente.");
            }
            bool hayTabla = TablaSegura();
            List<EnlaceEquivalenteCliente> enlaces = hayTabla
                ? await repositorio.LeerEnlaces(emp).ConfigureAwait(false) ?? new List<EnlaceEquivalenteCliente>()
                : new List<EnlaceEquivalenteCliente>();
            IReadOnlyList<string> grupo = ReglaClientesEquivalentes.Grupo(cli, Diccionario(enlaces));
            List<DatosFichaEquivalente> fichas = await repositorio.LeerFichas(emp, grupo).ConfigureAwait(false) ?? new List<DatosFichaEquivalente>();
            if (!fichas.Any(f => string.Equals(f.Cliente?.Trim(), cli, StringComparison.OrdinalIgnoreCase)))
            {
                return ResultadoEquivalencias<ClientesEquivalentesDTO>.Error(404, $"No existe el cliente {cli} en la empresa {emp}.");
            }
            return ResultadoEquivalencias<ClientesEquivalentesDTO>.Ok(new ClientesEquivalentesDTO
            {
                Empresa = emp,
                Cliente = cli,
                Fichas = ReglaEnlaceEquivalentes.Fichas(cli, grupo, fichas, enlaces)
            });
        }

        /// <param name="motivoSinPermiso">(vendedor de la otra ficha) → null si puede; si no, el motivo del 403.</param>
        public async Task<ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>> Enlazar(PeticionEnlaceEquivalenteDTO peticion, string usuario,
            Func<string, Task<string>> motivoSinPermiso)
        {
            string emp = Empresa(peticion?.Empresa);
            string cli = peticion?.Cliente?.Trim();
            string ant = peticion?.ClienteAnterior?.Trim();
            // Lo que no necesita la BD, primero
            DecisionEnlaceEquivalente previa = ReglaEnlaceEquivalentes.Decidir(cli, ant, peticion?.Motivo,
                new DatosFichaEquivalente(), new DatosFichaEquivalente(), null);
            if (previa.Estado == 400)
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(400, previa.Motivo);
            }
            if (!TablaSegura())
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(503, MENSAJE_SIN_TABLA);
            }
            List<DatosFichaEquivalente> fichas = await repositorio.LeerFichas(emp, new[] { cli, ant }).ConfigureAwait(false) ?? new List<DatosFichaEquivalente>();
            DatosFichaEquivalente fichaCliente = Buscar(fichas, cli);
            DatosFichaEquivalente fichaAnterior = Buscar(fichas, ant);
            if (fichaCliente != null && fichaAnterior != null && motivoSinPermiso != null)
            {
                string sinPermiso = await motivoSinPermiso(fichaAnterior.Vendedor).ConfigureAwait(false);
                if (sinPermiso != null)
                {
                    return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(403, sinPermiso);
                }
            }
            List<EnlaceEquivalenteCliente> enlaces = await repositorio.LeerEnlaces(emp).ConfigureAwait(false) ?? new List<EnlaceEquivalenteCliente>();
            DecisionEnlaceEquivalente decision = ReglaEnlaceEquivalentes.Decidir(cli, ant, peticion.Motivo, fichaCliente, fichaAnterior, Diccionario(enlaces));
            if (decision.Estado != 200)
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(decision.Estado, decision.Motivo, decision.Codigo);
            }
            var respuesta = new ResultadoEnlaceEquivalenteDTO
            {
                Empresa = emp,
                Cliente = cli,
                ClienteAnterior = ant,
                Motivo = decision.MotivoEnlace,
                YaEstaba = decision.YaEstaba,
                Encadenado = decision.Encadenado,
                Mensaje = decision.Mensaje,
                Avisos = decision.Avisos
            };
            if (!decision.YaEstaba)
            {
                string comentario = ReglaEnlaceEquivalentes.ComentarioNormalizado(peticion.Comentario);
                bool hecho = await repositorio.Enlazar(emp, decision.Cliente, decision.ClienteAnterior, decision.MotivoEnlace, comentario, usuario,
                    ReglaEnlaceEquivalentes.Rastro(decision.Cliente, decision.ClienteAnterior, decision.MotivoEnlace, comentario)).ConfigureAwait(false);
                if (!hecho)
                {
                    return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(409,
                        $"Alguien ha enlazado el {decision.Cliente} con otra ficha a la vez: vuelva a consultar sus equivalentes e inténtelo de nuevo.",
                        ReglaEnlaceEquivalentes.CODIGO_OCUPADO);
                }
                respuesta.Cambiado = true;
                respuesta.EnlaceCliente = decision.Cliente;
                respuesta.EnlaceClienteAnterior = decision.ClienteAnterior;
                enlaces = await repositorio.LeerEnlaces(emp).ConfigureAwait(false) ?? new List<EnlaceEquivalenteCliente>();
            }
            respuesta.Fichas = await FichasDelGrupo(emp, cli, enlaces).ConfigureAwait(false);
            return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Ok(respuesta);
        }

        /// <summary>Deshace el enlace <paramref name="cliente"/> → <paramref name="anterior"/> (404 si no es exactamente ese), con rastro.</summary>
        public async Task<ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>> Deshacer(string empresa, string cliente, string anterior, string usuario,
            Func<string, Task<string>> motivoSinPermiso)
        {
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            string ant = anterior?.Trim();
            if (string.IsNullOrEmpty(cli) || string.IsNullOrEmpty(ant))
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(400, "Indique el cliente y su anterior.");
            }
            if (!TablaSegura())
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(503, MENSAJE_SIN_TABLA);
            }
            List<EnlaceEquivalenteCliente> enlaces = await repositorio.LeerEnlaces(emp).ConfigureAwait(false) ?? new List<EnlaceEquivalenteCliente>();
            string motivo = ReglaEnlaceEquivalentes.MotivoNoSePuedeDeshacer(cli, ant, Diccionario(enlaces));
            if (motivo != null)
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(404, motivo);
            }
            if (motivoSinPermiso != null)
            {
                DatosFichaEquivalente fichaAnterior = Buscar(await repositorio.LeerFichas(emp, new[] { ant }).ConfigureAwait(false), ant);
                string sinPermiso = await motivoSinPermiso(fichaAnterior?.Vendedor).ConfigureAwait(false);
                if (sinPermiso != null)
                {
                    return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(403, sinPermiso);
                }
            }
            EnlaceEquivalenteCliente fila = enlaces.Where(e => string.Equals(e.Cliente?.Trim(), cli, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Fecha).First();
            string motivoEnlace = ReglaEnlaceEquivalentes.NormalizarMotivo(fila.Motivo) ?? fila.Motivo?.Trim() ?? "sin motivo";
            bool hecho = await repositorio.Deshacer(emp, cli, ant, usuario,
                ReglaEnlaceEquivalentes.Rastro(cli, ant, motivoEnlace, fila.Comentario?.Trim())).ConfigureAwait(false);
            if (!hecho)
            {
                return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Error(404, $"El {cli} ya no tiene como anterior al {ant}: no hay nada que deshacer.");
            }
            enlaces = await repositorio.LeerEnlaces(emp).ConfigureAwait(false) ?? new List<EnlaceEquivalenteCliente>();
            return ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO>.Ok(new ResultadoEnlaceEquivalenteDTO
            {
                Empresa = emp,
                Cliente = cli,
                ClienteAnterior = ant,
                Motivo = ReglaEnlaceEquivalentes.NormalizarMotivo(fila.Motivo),
                Cambiado = true,
                EnlaceCliente = cli,
                EnlaceClienteAnterior = ant,
                Mensaje = $"Deshecho el enlace: el {cli} ya no tiene como anterior al {ant}.",
                Fichas = await FichasDelGrupo(emp, cli, enlaces).ConfigureAwait(false)
            });
        }

        private async Task<List<FichaEquivalenteDTO>> FichasDelGrupo(string empresa, string cliente, List<EnlaceEquivalenteCliente> enlaces)
        {
            IReadOnlyList<string> grupo = ReglaClientesEquivalentes.Grupo(cliente, Diccionario(enlaces));
            List<DatosFichaEquivalente> fichas = await repositorio.LeerFichas(empresa, grupo).ConfigureAwait(false);
            return ReglaEnlaceEquivalentes.Fichas(cliente, grupo, fichas, enlaces);
        }

        private bool TablaSegura()
        {
            try
            {
                return repositorio.TablaExiste();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Clientes equivalentes] No se ha podido comprobar dbo.ClientesAnteriores: {ex.Message}", ex));
                return false;
            }
        }

        private static IReadOnlyDictionary<string, string> Diccionario(IEnumerable<EnlaceEquivalenteCliente> enlaces)
            => ReglaClientesEquivalentes.Diccionario((enlaces ?? Enumerable.Empty<EnlaceEquivalenteCliente>())
                .Select(e => new EnlaceClienteAnterior { Cliente = e.Cliente, ClienteAnterior = e.ClienteAnterior, Fecha = e.Fecha, Usuario = e.Usuario }));

        private static DatosFichaEquivalente Buscar(IEnumerable<DatosFichaEquivalente> fichas, string cliente)
            => (fichas ?? Enumerable.Empty<DatosFichaEquivalente>()).FirstOrDefault(f => string.Equals(f?.Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase));

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
