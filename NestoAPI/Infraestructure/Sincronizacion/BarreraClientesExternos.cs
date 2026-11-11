using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>Lo que la barrera no ha dejado pasar (para la traza: sin datos personales, solo cliente, campo y motivo).</summary>
    public class CampoDescartadoSincronizacion
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Null en los campos del cliente; el Id de la persona de contacto en los suyos.</summary>
        public string Persona { get; set; }
        public string Campo { get; set; }
        public string Motivo { get; set; }

        public override string ToString() =>
            $"[Barrera clientes externos] Cliente {Cliente}-{Contacto}{(Persona == null ? string.Empty : $", persona {Persona}")}: " +
            $"{Campo} descartado ({Motivo})";
    }

    /// <summary>La traza informativa en ELMAH de un campo descartado (agrupable por el texto: cliente, campo y motivo).</summary>
    public class CampoDescartadoSincronizacionException : Exception
    {
        public CampoDescartadoSincronizacionException(CampoDescartadoSincronizacion descartado) : base(descartado?.ToString())
        {
            Descartado = descartado;
        }

        public CampoDescartadoSincronizacion Descartado { get; }
    }

    /// <summary>
    /// 11/11/26 (eco de Odoo, odoo-custom-addons#48): Odoo publicó hacia Nesto clientes que nadie había editado, con los datos
    /// transformados a su manera, y se aplicaron. Desde aquí, a lo que llega de fuera (Source distinto de Nesto) solo se le aplican
    /// los campos que de verdad son distintos y que pasan estas reglas:
    /// <list type="bullet">
    /// <item>Comentarios: nunca con etiquetas HTML ni con líneas que empiezan por «[» (las «[Teléfonos extra] …» de Odoo).</item>
    /// <item>Teléfono: solo si trae algún número que Nesto no tenga (no si son los mismos, en otro orden, o solo una parte).</item>
    /// <item>Persona de contacto: el Nombre no puede ser su correo (el recibido o el de Nesto); el teléfono, como el del cliente.</item>
    /// </list>
    /// El vendedor no se toca aquí (ver <see cref="ClientesSyncHandler"/>).
    /// </summary>
    public static class BarreraClientesExternos
    {
        public const string CAMPO_NOMBRE = "Nombre";
        public const string CAMPO_TELEFONO = "Teléfono";
        public const string CAMPO_DIRECCION = "Dirección";
        public const string CAMPO_POBLACION = "Población";
        public const string CAMPO_CODIGO_POSTAL = "CodPostal";
        public const string CAMPO_PROVINCIA = "Provincia";
        public const string CAMPO_NIF = "CIF/NIF";
        public const string CAMPO_COMENTARIOS = "Comentarios";
        public const string CAMPO_EMAIL = "Email";

        public const string MOTIVO_COMENTARIOS_ODOO = "HTML o líneas «[…]» añadidas por Odoo";
        public const string MOTIVO_TELEFONO_SIN_NUEVOS = "no trae ningún número nuevo";
        public const string MOTIVO_NOMBRE_ES_CORREO = "el nombre es el correo";

        private static readonly Regex EtiquetaHtml = new Regex(@"<\s*/?\s*[a-zA-Z][^>]*>", RegexOptions.CultureInvariant);
        private static readonly char[] SeparadoresTelefono = { '/', ';', ',', '|', '\n', '\r' };

        /// <summary>¿Lo manda Nesto? (esos mensajes ni llegan aquí: el router los descarta antes).</summary>
        public static bool EsDeNesto(string source) => SyncTableRouter.EsMensajePropio(source);

        // ---------- Reglas ----------

        /// <summary>Comentarios que Odoo ha transformado: con etiquetas HTML o con alguna línea que empieza por «[».</summary>
        public static bool ComentarioTransformado(string recibido)
        {
            if (string.IsNullOrWhiteSpace(recibido))
            {
                return false;
            }
            if (EtiquetaHtml.IsMatch(recibido))
            {
                return true;
            }
            return recibido.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Any(l => l.TrimStart().StartsWith("[", StringComparison.Ordinal));
        }

        /// <summary>
        /// True si el teléfono recibido no trae ningún número que no tenga ya Nesto: los mismos (en otro orden o con otros
        /// separadores) o solo una parte. Se comparan solo los dígitos de cada número (sin el prefijo 34 / 0034).
        /// </summary>
        public static bool TelefonoSinNumerosNuevos(string enNesto, string recibido)
        {
            List<string> recibidos = Numeros(recibido);
            if (recibidos.Count == 0)
            {
                return false; // sin números (vacío o solo letras): la regla de siempre decide
            }
            List<string> nesto = Numeros(enNesto);
            return recibidos.All(r => nesto.Contains(r));
        }

        /// <summary>El nombre recibido es el correo de la persona (el que llega o el que tiene Nesto).</summary>
        public static bool NombreEsCorreo(string nombreRecibido, string correoRecibido, string correoNesto)
        {
            string nombre = nombreRecibido?.Trim();
            if (string.IsNullOrEmpty(nombre))
            {
                return false;
            }
            return string.Equals(nombre, correoRecibido?.Trim(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(nombre, correoNesto?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Los números de un campo de teléfono, solo dígitos y sin prefijo de España.</summary>
        internal static List<string> Numeros(string telefono)
        {
            var numeros = new List<string>();
            if (string.IsNullOrWhiteSpace(telefono))
            {
                return numeros;
            }
            foreach (string trozo in telefono.Split(SeparadoresTelefono, StringSplitOptions.RemoveEmptyEntries))
            {
                string digitos = SoloDigitos(trozo);
                if (digitos.Length == 0)
                {
                    continue;
                }
                // «916281914 600123456»: dos números separados solo por espacio (cada parte con pinta de teléfono).
                string[] palabras = trozo.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> porPalabras = palabras.Select(SoloDigitos).Where(d => d.Length > 0).ToList();
                if (porPalabras.Count > 1 && porPalabras.All(d => d.Length >= 9))
                {
                    numeros.AddRange(porPalabras.Select(SinPrefijo));
                }
                else
                {
                    numeros.Add(SinPrefijo(digitos));
                }
            }
            return numeros.Distinct().ToList();
        }

        private static string SoloDigitos(string texto) => new string((texto ?? string.Empty).Where(char.IsDigit).ToArray());

        private static string SinPrefijo(string digitos)
        {
            if (digitos.Length == 13 && digitos.StartsWith("0034", StringComparison.Ordinal))
            {
                return digitos.Substring(4);
            }
            if (digitos.Length == 11 && digitos.StartsWith("34", StringComparison.Ordinal))
            {
                return digitos.Substring(2);
            }
            return digitos;
        }

        // ---------- Qué se aplica ----------

        /// <summary>
        /// Los campos del cliente que se aplican: los distintos (con la misma comparación que <see cref="ClienteChangeDetector"/>) que
        /// llegan con contenido y que pasan la barrera. Lo que la barrera descarta va a <paramref name="descartados"/>.
        /// </summary>
        public static HashSet<string> CamposAAplicar(Cliente nesto, ClienteSyncMessage recibido, List<CampoDescartadoSincronizacion> descartados)
        {
            var campos = new HashSet<string>(StringComparer.Ordinal);
            if (nesto == null || recibido == null)
            {
                return campos;
            }
            var detector = new ClienteChangeDetector();
            HashSet<string> distintos = detector.CamposDistintos(nesto, recibido);
            bool externo = !EsDeNesto(recibido.Source);

            void Si(string campo, string valor, Func<string> motivoDescarte)
            {
                if (!distintos.Contains(campo) || string.IsNullOrWhiteSpace(valor))
                {
                    return;
                }
                string motivo = externo ? motivoDescarte?.Invoke() : null;
                if (motivo != null)
                {
                    descartados?.Add(new CampoDescartadoSincronizacion
                    {
                        Cliente = recibido.Cliente?.Trim(),
                        Contacto = recibido.Contacto?.Trim(),
                        Campo = campo,
                        Motivo = motivo
                    });
                    return;
                }
                campos.Add(campo);
            }

            Si(CAMPO_NOMBRE, recibido.Nombre, null);
            Si(CAMPO_TELEFONO, recibido.Telefono, () => TelefonoSinNumerosNuevos(nesto.Teléfono, recibido.Telefono) ? MOTIVO_TELEFONO_SIN_NUEVOS : null);
            Si(CAMPO_DIRECCION, recibido.Direccion, null);
            Si(CAMPO_POBLACION, recibido.Poblacion, null);
            Si(CAMPO_CODIGO_POSTAL, recibido.CodigoPostal, null);
            Si(CAMPO_PROVINCIA, recibido.Provincia, null);
            Si(CAMPO_NIF, recibido.Nif, null);
            Si(CAMPO_COMENTARIOS, recibido.Comentarios, () => ComentarioTransformado(recibido.Comentarios) ? MOTIVO_COMENTARIOS_ODOO : null);
            return campos;
        }

        /// <summary>Aplica al cliente solo esos campos (y la marca de modificación si hay alguno). Devuelve si ha tocado algo.</summary>
        public static bool Aplicar(Cliente nesto, ClienteSyncMessage recibido, ICollection<string> campos)
        {
            if (nesto == null || recibido == null || campos == null || campos.Count == 0)
            {
                return false;
            }
            if (campos.Contains(CAMPO_NOMBRE)) nesto.Nombre = recibido.Nombre;
            if (campos.Contains(CAMPO_TELEFONO)) nesto.Teléfono = recibido.Telefono;
            if (campos.Contains(CAMPO_DIRECCION)) nesto.Dirección = recibido.Direccion;
            if (campos.Contains(CAMPO_POBLACION)) nesto.Población = recibido.Poblacion;
            if (campos.Contains(CAMPO_CODIGO_POSTAL)) nesto.CodPostal = recibido.CodigoPostal;
            if (campos.Contains(CAMPO_PROVINCIA)) nesto.Provincia = recibido.Provincia;
            if (campos.Contains(CAMPO_NIF)) nesto.CIF_NIF = recibido.Nif;
            if (campos.Contains(CAMPO_COMENTARIOS)) nesto.Comentarios = recibido.Comentarios;
            nesto.Fecha_Modificación = DateTime.Now;
            nesto.Usuario = "EXTERNAL_SYNC";
            return true;
        }

        /// <summary>Lo mismo para una persona de contacto (Nombre, Teléfono y Email).</summary>
        public static HashSet<string> CamposAAplicarPersona(PersonaContactoCliente nesto, PersonaContactoSyncDTO recibida, string source,
            string cliente, string contacto, List<CampoDescartadoSincronizacion> descartados)
        {
            var campos = new HashSet<string>(StringComparer.Ordinal);
            if (nesto == null || recibida == null)
            {
                return campos;
            }
            HashSet<string> distintos = new ClienteChangeDetector().CamposDistintosPersonaContacto(nesto, recibida);
            bool externo = !EsDeNesto(source);

            void Si(string campo, string valor, Func<string> motivoDescarte)
            {
                if (!distintos.Contains(campo) || string.IsNullOrWhiteSpace(valor))
                {
                    return;
                }
                string motivo = externo ? motivoDescarte?.Invoke() : null;
                if (motivo != null)
                {
                    descartados?.Add(new CampoDescartadoSincronizacion
                    {
                        Cliente = cliente,
                        Contacto = contacto,
                        Persona = recibida.Id?.Trim(),
                        Campo = campo,
                        Motivo = motivo
                    });
                    return;
                }
                campos.Add(campo);
            }

            Si(CAMPO_NOMBRE, recibida.Nombre, () => NombreEsCorreo(recibida.Nombre, recibida.CorreoElectronico, nesto.CorreoElectrónico) ? MOTIVO_NOMBRE_ES_CORREO : null);
            Si(CAMPO_TELEFONO, recibida.Telefonos, () => TelefonoSinNumerosNuevos(nesto.Teléfono, recibida.Telefonos) ? MOTIVO_TELEFONO_SIN_NUEVOS : null);
            Si(CAMPO_EMAIL, recibida.CorreoElectronico, null);
            return campos;
        }

        public static bool AplicarPersona(PersonaContactoCliente nesto, PersonaContactoSyncDTO recibida, ICollection<string> campos)
        {
            if (nesto == null || recibida == null || campos == null || campos.Count == 0)
            {
                return false;
            }
            if (campos.Contains(CAMPO_NOMBRE)) nesto.Nombre = recibida.Nombre;
            if (campos.Contains(CAMPO_TELEFONO)) nesto.Teléfono = recibida.Telefonos;
            if (campos.Contains(CAMPO_EMAIL)) nesto.CorreoElectrónico = recibida.CorreoElectronico;
            nesto.Fecha_Modificación = DateTime.Now;
            nesto.Usuario = "EXTERNAL_SYNC";
            return true;
        }
    }
}
