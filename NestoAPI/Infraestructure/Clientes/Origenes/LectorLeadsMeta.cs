using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>NestoAPI#623 corte 4: un lead del fichero que exporta Meta Ads (formulario de la campaña).</summary>
    public class LeadMeta
    {
        /// <summary>Fila del fichero (la cabecera es la 1).</summary>
        public int Fila { get; set; }
        /// <summary>El «id» del lead en Meta: lo único que va a ELMAH (nunca nombre, teléfono ni correo).</summary>
        public string Id { get; set; }
        /// <summary>created_time pasado a la hora de España (Meta lo da en UTC).</summary>
        public DateTime? FechaCreacion { get; set; }
        /// <summary>fb o ig.</summary>
        public string Plataforma { get; set; }
        public string SituacionProfesional { get; set; }
        public string Solicita { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        /// <summary>Como se graba en la ficha: los 9 dígitos si es español (+34); si no, todos los dígitos.</summary>
        public string Telefono { get; set; }
        /// <summary>Las 9 últimas cifras, para buscar clientes con el mismo teléfono.</summary>
        public string TelefonoParaBuscar { get; set; }
        public string CodigoPostal { get; set; }
        /// <summary>La casilla «deseo recibir por correo electrónico novedades…». Sin columna o vacía = no acepta.</summary>
        public bool AceptaComunicaciones { get; set; }
    }

    /// <summary>
    /// NestoAPI#623 corte 4: lee el fichero de leads de Meta. Acepta el «Excel 2003 XML» (SpreadsheetML) y el CSV (UTF-16 con
    /// tabuladores o UTF-8 con comas o puntos y comas). Las cabeceras se reconocen normalizadas (sin tildes ni signos) y por
    /// prefijo o fragmento, porque las tildes pueden llegar estropeadas (Windows-1252 leído como UTF-8 o al revés).
    /// </summary>
    public static class LectorLeadsMeta
    {
        public const string NS_SPREADSHEET = "urn:schemas-microsoft-com:office:spreadsheet";
        public const string ZONA_ESPANA = "Romance Standard Time";

        public static List<LeadMeta> Leer(byte[] contenido)
        {
            return Leer(Decodificar(contenido));
        }

        public static List<LeadMeta> Leer(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                throw new FormatException("El fichero está vacío.");
            }
            string limpio = texto.TrimStart('﻿', ' ', '\r', '\n', '\t');
            List<List<string>> filas = limpio.StartsWith("<") ? FilasXml(limpio) : FilasCsv(limpio);
            filas = filas.Where(f => f.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
            if (filas.Count == 0)
            {
                throw new FormatException("El fichero no tiene filas.");
            }
            return Leads(filas);
        }

        /// <summary>BOM UTF-16/UTF-8; sin BOM, UTF-8 si es válido y, si no, Windows-1252 (así llegan las tildes de Meta).</summary>
        public static string Decodificar(byte[] contenido)
        {
            if (contenido == null || contenido.Length == 0)
            {
                return string.Empty;
            }
            if (contenido.Length >= 2 && contenido[0] == 0xFF && contenido[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(contenido, 2, contenido.Length - 2);
            }
            if (contenido.Length >= 2 && contenido[0] == 0xFE && contenido[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(contenido, 2, contenido.Length - 2);
            }
            if (contenido.Length >= 3 && contenido[0] == 0xEF && contenido[1] == 0xBB && contenido[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(contenido, 3, contenido.Length - 3);
            }
            // UTF-16 sin BOM: los bytes impares a cero.
            if (contenido.Length >= 4 && contenido[1] == 0 && contenido[3] == 0)
            {
                return Encoding.Unicode.GetString(contenido);
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(contenido);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1252).GetString(contenido);
            }
        }

        private static List<List<string>> FilasXml(string texto)
        {
            XDocument documento;
            try
            {
                documento = XDocument.Parse(texto);
            }
            catch (Exception ex)
            {
                throw new FormatException("El fichero no es un Excel XML válido: " + ex.Message, ex);
            }
            XNamespace ss = NS_SPREADSHEET;
            XElement hoja = documento.Descendants(ss + "Worksheet").FirstOrDefault();
            if (hoja == null)
            {
                throw new FormatException("El fichero XML no tiene ninguna hoja.");
            }
            var filas = new List<List<string>>();
            foreach (XElement fila in hoja.Descendants(ss + "Row"))
            {
                var celdas = new List<string>();
                foreach (XElement celda in fila.Elements(ss + "Cell"))
                {
                    // ss:Index = la celda salta columnas vacías.
                    string indice = (string)celda.Attribute(ss + "Index");
                    if (int.TryParse(indice, out int posicion))
                    {
                        while (celdas.Count < posicion - 1)
                        {
                            celdas.Add(string.Empty);
                        }
                    }
                    celdas.Add(celda.Element(ss + "Data")?.Value ?? string.Empty);
                }
                filas.Add(celdas);
            }
            return filas;
        }

        private static List<List<string>> FilasCsv(string texto)
        {
            string primeraLinea = texto.Split('\n')[0];
            char separador = primeraLinea.Contains('\t') ? '\t'
                : primeraLinea.Count(c => c == ';') > primeraLinea.Count(c => c == ',') ? ';' : ',';
            var filas = new List<List<string>>();
            var fila = new List<string>();
            var celda = new StringBuilder();
            bool entreComillas = false;
            for (int i = 0; i < texto.Length; i++)
            {
                char c = texto[i];
                if (entreComillas)
                {
                    if (c == '"')
                    {
                        if (i + 1 < texto.Length && texto[i + 1] == '"')
                        {
                            _ = celda.Append('"');
                            i++;
                        }
                        else
                        {
                            entreComillas = false;
                        }
                    }
                    else
                    {
                        _ = celda.Append(c);
                    }
                }
                else if (c == '"')
                {
                    entreComillas = true;
                }
                else if (c == separador)
                {
                    fila.Add(celda.ToString());
                    _ = celda.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    {
                        i++;
                    }
                    fila.Add(celda.ToString());
                    _ = celda.Clear();
                    filas.Add(fila);
                    fila = new List<string>();
                }
                else
                {
                    _ = celda.Append(c);
                }
            }
            if (celda.Length > 0 || fila.Count > 0)
            {
                fila.Add(celda.ToString());
                filas.Add(fila);
            }
            return filas;
        }

        /// <summary>La tabla (cabecera + filas) → leads. También la usa la fuente Graph (NestoAPI#623 corte 5c, <see cref="LectorGraphLeadsMeta"/>).</summary>
        internal static List<LeadMeta> Leads(List<List<string>> filas)
        {
            List<string> cabecera = filas[0].Select(NormalizarCabecera).ToList();
            int id = Columna(cabecera, c => c == "id" || c == "lead_id");
            if (id < 0)
            {
                throw new FormatException("El fichero no tiene la columna «id» de Meta: ¿es la exportación de los leads del formulario?");
            }
            int fecha = Columna(cabecera, c => c == "created_time");
            int plataforma = Columna(cabecera, c => c == "platform");
            int situacion = Columna(cabecera, c => c.Contains("situaci"));
            int solicita = Columna(cabecera, c => c.Contains("solicitar"));
            int nombre = Columna(cabecera, c => c == "full_name" || c == "nombre_completo");
            int email = Columna(cabecera, c => c == "email" || c.StartsWith("correo"));
            int telefono = Columna(cabecera, c => c == "phone_number" || c.StartsWith("telefono") || c.StartsWith("numero_de_telefono"));
            int codigoPostal = Columna(cabecera, c => c == "post_code" || c == "zip_code" || c == "zip" || c.StartsWith("codigo_postal"));
            int consentimiento = Columna(cabecera, c => c.StartsWith("deseo_recibir") || c.Contains("recibir_por_correo"));

            var leads = new List<LeadMeta>();
            for (int i = 1; i < filas.Count; i++)
            {
                List<string> f = filas[i];
                string Valor(int columna) => columna >= 0 && columna < f.Count ? f[columna]?.Trim() : null;
                (string paraFicha, string paraBuscar) = NormalizarTelefono(Valor(telefono));
                leads.Add(new LeadMeta
                {
                    Fila = i + 1,
                    Id = LimpiarPrefijo(Valor(id)),
                    FechaCreacion = FechaEspana(Valor(fecha)),
                    Plataforma = Valor(plataforma),
                    SituacionProfesional = TextoRespuesta(Valor(situacion)),
                    Solicita = TextoRespuesta(Valor(solicita)),
                    Nombre = Valor(nombre),
                    Email = string.IsNullOrWhiteSpace(Valor(email)) ? null : Valor(email).ToLowerInvariant(),
                    Telefono = paraFicha,
                    TelefonoParaBuscar = paraBuscar,
                    CodigoPostal = NormalizarCodigoPostal(Valor(codigoPostal)),
                    AceptaComunicaciones = EsSi(Valor(consentimiento))
                });
            }
            return leads;
        }

        private static int Columna(List<string> cabecera, Func<string, bool> criterio)
        {
            for (int i = 0; i < cabecera.Count; i++)
            {
                if (criterio(cabecera[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Minúsculas, sin tildes y solo [a-z0-9_]: «¿Cuál_es_tu_situación…» → «cual_es_tu_situacion…».</summary>
        public static string NormalizarCabecera(string cabecera)
        {
            if (string.IsNullOrWhiteSpace(cabecera))
            {
                return string.Empty;
            }
            string descompuesta = cabecera.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var resultado = new StringBuilder();
            foreach (char c in descompuesta)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                {
                    _ = resultado.Append(c);
                }
                else if (c == ' ')
                {
                    _ = resultado.Append('_');
                }
            }
            return resultado.ToString().Trim('_');
        }

        /// <summary>
        /// Meta da «+34612345678» (en CSV a veces «p:+34612345678»). Para la ficha: los 9 dígitos si es de España; si no, todos.
        /// Para buscar: las 9 últimas cifras.
        /// </summary>
        public static (string ParaFicha, string ParaBuscar) NormalizarTelefono(string telefono)
        {
            if (string.IsNullOrWhiteSpace(telefono))
            {
                return (null, null);
            }
            string digitos = new string(LimpiarPrefijo(telefono).Where(char.IsDigit).ToArray());
            if (digitos.StartsWith("00"))
            {
                digitos = digitos.Substring(2);
            }
            if (digitos.Length == 11 && digitos.StartsWith("34"))
            {
                digitos = digitos.Substring(2);
            }
            if (digitos.Length == 0)
            {
                return (null, null);
            }
            string paraFicha = digitos.Length > 29 ? digitos.Substring(0, 29) : digitos;
            string paraBuscar = digitos.Length > 9 ? digitos.Substring(digitos.Length - 9) : digitos;
            return (paraFicha, paraBuscar);
        }

        /// <summary>Solo las cifras; «3330» → «03330» (Excel se come el cero de delante). Lo que no quede en 5 cifras lo avisa el importador.</summary>
        public static string NormalizarCodigoPostal(string codigoPostal)
        {
            string limpio = LimpiarPrefijo(codigoPostal);
            if (string.IsNullOrWhiteSpace(limpio))
            {
                return null;
            }
            string cifras = new string(limpio.Where(char.IsDigit).ToArray());
            if (cifras.Length == 0)
            {
                return limpio.Trim();
            }
            return cifras.Length == 4 ? "0" + cifras : cifras;
        }

        /// <summary>Un código postal español válido: 5 cifras.</summary>
        public static bool EsCodigoPostalValido(string codigoPostal) =>
            codigoPostal != null && codigoPostal.Length == 5 && codigoPostal.All(char.IsDigit);

        /// <summary>«tengo_un_centro_de_estética_» → «tengo un centro de estética».</summary>
        public static string TextoRespuesta(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return null;
            }
            string texto = respuesta.Replace('_', ' ').Trim(' ', '-', '.');
            while (texto.Contains("  "))
            {
                texto = texto.Replace("  ", " ");
            }
            return texto.Trim(' ', '-', '.');
        }

        /// <summary>Meta pone «p:», «e:»… delante de algunos valores en el CSV.</summary>
        private static string LimpiarPrefijo(string valor)
        {
            if (valor == null)
            {
                return null;
            }
            string v = valor.Trim();
            return v.Length > 2 && v[1] == ':' && char.IsLetter(v[0]) && !v.Contains("@") ? v.Substring(2).Trim() : v;
        }

        private static bool EsSi(string valor)
        {
            string v = valor?.Trim().ToLowerInvariant();
            return v == "true" || v == "yes" || v == "si" || v == "sí" || v == "1" || v == "verdadero";
        }

        /// <summary>created_time (UTC si no lleva zona) → hora de España.</summary>
        public static DateTime? FechaEspana(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }
            if (!DateTimeOffset.TryParse(valor.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset fecha))
            {
                return null;
            }
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(fecha.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(ZONA_ESPANA));
            }
            catch (TimeZoneNotFoundException)
            {
                return fecha.UtcDateTime.ToLocalTime();
            }
        }
    }
}
