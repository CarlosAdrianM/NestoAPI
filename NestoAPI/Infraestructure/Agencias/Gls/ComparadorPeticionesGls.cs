using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>NestoAPI#552: un campo en el que la petición de la API no coincide con la de Nesto.</summary>
    public class DiferenciaPeticionGls
    {
        /// <summary>Ruta dentro de Servicios, p. ej. «Envio/Destinatario/CP» o «@uidcliente».</summary>
        public string Campo { get; set; }
        public string Nesto { get; set; }
        public string Api { get; set; }

        public override string ToString() => $"{Campo}: Nesto «{Nesto}» / API «{Api}»";
    }

    /// <summary>
    /// NestoAPI#552: compara dos nodos Servicios campo a campo (hojas y atributos; los comentarios no cuentan). La
    /// fecha y el reembolso se comparan por su valor, no por el texto: hay puestos de Nesto que mandan el año con
    /// cuatro cifras, y el reembolso sale con los decimales que traiga la columna.
    /// </summary>
    public static class ComparadorPeticionesGls
    {
        private static readonly string[] FORMATOS_FECHA = { "dd/MM/yy", "dd/MM/yyyy", "d/M/yy", "d/M/yyyy" };

        public static List<DiferenciaPeticionGls> Comparar(XElement nesto, XElement api)
        {
            Dictionary<string, string> camposNesto = Aplanar(nesto);
            Dictionary<string, string> camposApi = Aplanar(api);

            return camposNesto.Keys.Union(camposApi.Keys)
                .Select(campo => new DiferenciaPeticionGls
                {
                    Campo = campo,
                    Nesto = camposNesto.TryGetValue(campo, out string n) ? n : null,
                    Api = camposApi.TryGetValue(campo, out string a) ? a : null
                })
                .Where(d => d.Nesto == null || d.Api == null || Normalizar(d.Campo, d.Nesto) != Normalizar(d.Campo, d.Api))
                .ToList();
        }

        private static Dictionary<string, string> Aplanar(XElement raiz)
        {
            var campos = new Dictionary<string, string>(StringComparer.Ordinal);
            if (raiz == null)
            {
                return campos;
            }
            foreach (XElement elemento in raiz.DescendantsAndSelf())
            {
                string ruta = Ruta(raiz, elemento);
                foreach (XAttribute atributo in elemento.Attributes().Where(a => !a.IsNamespaceDeclaration))
                {
                    Añadir(campos, ruta + "@" + atributo.Name.LocalName, atributo.Value);
                }
                if (elemento != raiz && !elemento.HasElements)
                {
                    Añadir(campos, ruta, elemento.Value);
                }
            }
            return campos;
        }

        private static string Ruta(XElement raiz, XElement elemento)
        {
            if (elemento == raiz)
            {
                return string.Empty;
            }
            return string.Join("/", elemento.AncestorsAndSelf().TakeWhile(e => e != raiz).Reverse().Select(e => e.Name.LocalName));
        }

        private static void Añadir(Dictionary<string, string> campos, string clave, string valor)
        {
            string libre = clave;
            for (int i = 2; campos.ContainsKey(libre); i++)
            {
                libre = $"{clave}#{i}";
            }
            campos[libre] = valor;
        }

        public const string CAMPO_FECHA = "Envio/Fecha";

        /// <summary>La fecha de una petición, mande Nesto el año con dos cifras o con cuatro.</summary>
        public static bool LeerFecha(string valor, out DateTime fecha)
            => DateTime.TryParseExact(valor, FORMATOS_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);

        private static string Normalizar(string campo, string valor)
        {
            if (campo == CAMPO_FECHA && LeerFecha(valor, out DateTime fecha))
            {
                return fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (campo == "Envio/Importes/Reembolso" &&
                decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal importe))
            {
                return importe.ToString("G29", CultureInfo.InvariantCulture);
            }
            // Al leer el XML guardado, \r\n y \r suelto llegan como \n (normalización de fin de línea de XML); en la BD
            // siguen como están (Observaciones es varchar(80) y a veces el corte deja un \r suelto al final).
            return valor?.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
