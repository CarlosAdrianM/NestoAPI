using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// Nesto#644 (5): lo que hay HOY en la base de datos de lo que la ficha comercial puede cambiar, con quién y cuándo lo
    /// tocó por última vez (la ficha del cliente y su fila de vendedor por grupo, cada una con lo suyo).
    /// </summary>
    internal class GuardadoClienteComercial
    {
        public short? Estado { get; set; }
        public string Vendedor { get; set; }
        public string Pais { get; set; }
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
        public bool FilaGrupoExiste { get; set; }
        public string VendedorPeluqueria { get; set; }
        public short? EstadoPeluqueria { get; set; }
        public string UsuarioGrupo { get; set; }
        public DateTime? FechaGrupo { get; set; }

        public static GuardadoClienteComercial De(Cliente cliente, VendedorClienteGrupoProducto filaGrupo)
        {
            return new GuardadoClienteComercial
            {
                Estado = cliente?.Estado,
                Vendedor = cliente?.Vendedor,
                Pais = cliente?.Pais,
                Usuario = cliente?.Usuario,
                Fecha = cliente?.Fecha_Modificación,
                FilaGrupoExiste = filaGrupo != null,
                VendedorPeluqueria = filaGrupo?.Vendedor,
                EstadoPeluqueria = filaGrupo?.Estado,
                UsuarioGrupo = filaGrupo?.Usuario,
                FechaGrupo = filaGrupo?.FechaModificacion
            };
        }
    }

    /// <summary>Nesto#644 (5): el porqué del 409 CLIENTE_CAMBIADO.</summary>
    internal class ConflictoClienteComercial
    {
        /// <summary>estado, vendedor, pais, vendedorPeluqueria, estadoPeluqueria (los que el PUT cambia y otro ya había cambiado).</summary>
        public List<string> Campos { get; set; } = new List<string>();
        public string Usuario { get; set; }
        public DateTime? Fecha { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Nesto#644 (5): LA REGLA del guardado con copia vieja de la ficha comercial (como NestoAPI#873 en los pedidos, pero por
    /// campo): para cada campo que el PUT CAMBIA (con el mismo criterio con que luego se aplica), si en la base de datos ya
    /// no vale lo que dice <see cref="ClienteDTO.original"/>, no se escribe nada. Lo que el PUT no cambia no cuenta aunque otro
    /// lo haya cambiado (no se pisa). Sin <c>original</c>, nunca hay conflicto (los llamantes de antes).
    /// <para>Textos: sin relleno ni mayúsculas, null = vacío. Estados: el de <c>original</c> a null no se compara. Peluquería:
    /// si la fila no existe, coincide con un original sin vendedor de peluquería (Nesto manda estado 0 cuando no hay fila).</para>
    /// </summary>
    internal static class ReglaClienteComercialCambiado
    {
        public const string CODIGO = "CLIENTE_CAMBIADO";

        public static ConflictoClienteComercial Evaluar(GuardadoClienteComercial guardado, ClienteDTO cambios, DateTime ahora)
        {
            OriginalClienteComercialDTO original = cambios?.original;
            if (original == null || guardado == null)
            {
                return null;
            }

            List<string> campos = new List<string>();
            bool tocaFicha = false;
            bool tocaGrupo = false;

            if (cambios.estado != null && guardado.Estado != cambios.estado
                && original.estado.HasValue && guardado.Estado != original.estado)
            {
                campos.Add("estado");
                tocaFicha = true;
            }
            // Mismo criterio que AplicarCambiosClienteComercial: sin vendedor guardado no se cambia
            if (guardado.Vendedor != null && cambios.vendedor != null && !MismoTexto(guardado.Vendedor, cambios.vendedor)
                && !MismoTexto(guardado.Vendedor, original.vendedor))
            {
                campos.Add("vendedor");
                tocaFicha = true;
            }
            if (ValidadorCambioClienteComercial.CambiaElPais(guardado.Pais, cambios.pais)
                && !MismoTexto(guardado.Pais, original.pais))
            {
                campos.Add("pais");
                tocaFicha = true;
            }

            VendedorGrupoProductoDTO filaNueva = cambios.VendedoresGrupoProducto?.FirstOrDefault();
            if (filaNueva != null)
            {
                bool seraCreada = !guardado.FilaGrupoExiste && !string.IsNullOrWhiteSpace(filaNueva.vendedor);
                bool cambiaVendedorGrupo = guardado.FilaGrupoExiste
                    ? filaNueva.vendedor != null && !MismoTexto(guardado.VendedorPeluqueria, filaNueva.vendedor)
                    : seraCreada;
                bool cambiaEstadoGrupo = guardado.FilaGrupoExiste
                    ? filaNueva.estado.HasValue && filaNueva.estado != guardado.EstadoPeluqueria
                    : seraCreada && filaNueva.estado.HasValue;

                if (cambiaVendedorGrupo && !MismoTexto(guardado.VendedorPeluqueria, original.vendedorPeluqueria))
                {
                    campos.Add("vendedorPeluqueria");
                    tocaGrupo = true;
                }
                if (cambiaEstadoGrupo && original.estadoPeluqueria.HasValue && !EstadoPeluqueriaCoincide(guardado, original))
                {
                    campos.Add("estadoPeluqueria");
                    tocaGrupo = true;
                }
            }

            if (!campos.Any())
            {
                return null;
            }

            // Quién y cuándo: lo más reciente de lo que choca
            string usuario = null;
            DateTime? fecha = null;
            if (tocaFicha)
            {
                usuario = guardado.Usuario;
                fecha = guardado.Fecha;
            }
            if (tocaGrupo && (fecha == null || (guardado.FechaGrupo.HasValue && guardado.FechaGrupo > fecha)))
            {
                usuario = guardado.UsuarioGrupo;
                fecha = guardado.FechaGrupo;
            }

            return new ConflictoClienteComercial
            {
                Campos = campos,
                Usuario = usuario?.Trim(),
                Fecha = fecha,
                Mensaje = Mensaje(cambios.cliente, usuario, fecha, campos, ahora)
            };
        }

        private static bool EstadoPeluqueriaCoincide(GuardadoClienteComercial guardado, OriginalClienteComercialDTO original)
        {
            if (!guardado.FilaGrupoExiste)
            {
                return string.IsNullOrWhiteSpace(original.vendedorPeluqueria);
            }
            return guardado.EstadoPeluqueria == original.estadoPeluqueria;
        }

        internal static string Mensaje(string cliente, string usuario, DateTime? fecha, List<string> campos, DateTime ahora)
        {
            string quien = UsuarioAuditoriaHelper.SinDominio(usuario?.Trim());
            if (string.IsNullOrWhiteSpace(quien))
            {
                quien = "otro usuario";
            }
            string cuando = string.Empty;
            if (fecha.HasValue)
            {
                cuando = fecha.Value.Date == ahora.Date
                    ? $" a las {fecha.Value:HH:mm}"
                    : $" el {fecha.Value:dd/MM} a las {fecha.Value:HH:mm}";
            }
            string nombres = string.Join(", ", campos.Select(NombreCampo));
            return $"La ficha del cliente {cliente?.Trim()} la ha cambiado {quien}{cuando} desde que usted la abrió ({nombres}). " +
                "Recárguela y vuelva a hacer los cambios.";
        }

        private static string NombreCampo(string campo)
        {
            switch (campo)
            {
                case "pais": return "país";
                case "vendedorPeluqueria": return "vendedor de peluquería";
                case "estadoPeluqueria": return "estado de peluquería";
                default: return campo;
            }
        }

        private static bool MismoTexto(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
