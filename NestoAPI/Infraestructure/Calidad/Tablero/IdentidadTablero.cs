using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    /// <summary>Un dueño concreto: (tipo, código).</summary>
    public class DuenoKpi
    {
        public DuenoKpi(TipoDuenoKpi tipo, string dueno)
        {
            Tipo = tipo;
            Dueno = dueno?.Trim().ToUpperInvariant();
        }

        public TipoDuenoKpi Tipo { get; }
        public string Dueno { get; }

        public bool Es(TipoDuenoKpi tipo, string dueno) => Tipo == tipo && string.Equals(Dueno, dueno?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// «Quién soy» para el tablero (NestoAPI#914): de qué dueños son los KPI de quien llama. Un usuario solo ve lo suyo:
    /// <list type="bullet">
    /// <item>Vendedor: sus vendedores, buscados como en «clientes para contactar» (ServicioVendedoresVisibles.VendedoresDelUsuario: parámetro de
    /// usuario «Vendedor», claim «Vendedor» de NestoApp y UsuarioVendedor).</item>
    /// <item>Almacén: su AlmacénPedidoVta (ParametrosUsuario), solo si es del grupo Almacén (ALG si no lo tiene) o Tiendas (sin él, ninguno).</item>
    /// <item>Preparador: su usuario sin dominio (el de Ariadna), si es del grupo Almacén o Tiendas.</item>
    /// <item>Empresa: Dirección.</item>
    /// </list>
    /// </summary>
    public class IdentidadTablero
    {
        public string Usuario { get; set; }
        public bool EsDireccion { get; set; }
        /// <summary>Dirección o Informática: puede ver los de todos (vista aparte).</summary>
        public bool PuedeVerTodo { get; set; }
        public List<string> Vendedores { get; set; } = new List<string>();
        public string Almacen { get; set; }
        public string Preparador { get; set; }

        /// <summary>Los dueños de este usuario para un KPI (en el orden de los tipos del KPI).</summary>
        public List<DuenoKpi> Duenos(IKpiTablero kpi)
        {
            var duenos = new List<DuenoKpi>();
            foreach (TipoDuenoKpi tipo in kpi?.TiposDueno ?? new List<TipoDuenoKpi>())
            {
                switch (tipo)
                {
                    case TipoDuenoKpi.Empresa:
                        if (EsDireccion)
                        {
                            duenos.Add(new DuenoKpi(tipo, Constantes.Empresas.EMPRESA_POR_DEFECTO));
                        }
                        break;
                    case TipoDuenoKpi.Almacen:
                        if (!string.IsNullOrWhiteSpace(Almacen))
                        {
                            duenos.Add(new DuenoKpi(tipo, Almacen));
                        }
                        break;
                    case TipoDuenoKpi.Vendedor:
                        duenos.AddRange((Vendedores ?? new List<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => new DuenoKpi(tipo, v)));
                        break;
                    case TipoDuenoKpi.Preparador:
                        if (!string.IsNullOrWhiteSpace(Preparador))
                        {
                            duenos.Add(new DuenoKpi(tipo, Preparador));
                        }
                        break;
                }
            }
            return duenos;
        }

        public bool EsSuyo(IKpiTablero kpi, TipoDuenoKpi tipo, string dueno) => Duenos(kpi).Any(d => d.Es(tipo, dueno));
    }

    public static class ResolutorIdentidadTablero
    {
        public const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        /// <param name="vendedores">El usuario → sus vendedores (en producción, ServicioVendedoresVisibles.VendedoresDelUsuario).</param>
        /// <param name="almacenDelUsuario">Usuario sin dominio → su AlmacénPedidoVta (null si no tiene).</param>
        public static async Task<IdentidadTablero> Resolver(IPrincipal usuario, Func<IPrincipal, Task<List<string>>> vendedores,
            Func<string, string> almacenDelUsuario)
        {
            string nombre = UsuarioAuditoriaHelper.SinDominio(usuario?.Identity?.Name)?.Trim();
            var identidad = new IdentidadTablero { Usuario = nombre };
            if (usuario?.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(nombre))
            {
                return identidad;
            }
            identidad.EsDireccion = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);
            identidad.PuedeVerTodo = identidad.EsDireccion || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA);
            identidad.Vendedores = (vendedores == null ? null : await vendedores(usuario).ConfigureAwait(false)) ?? new List<string>();
            bool almacen = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN);
            bool tiendas = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS);
            if (almacen || tiendas)
            {
                string suyo = almacenDelUsuario?.Invoke(nombre)?.Trim().ToUpperInvariant();
                identidad.Almacen = !string.IsNullOrEmpty(suyo) ? suyo : (almacen ? Constantes.Almacenes.ALGETE : null);
                identidad.Preparador = nombre;
            }
            return identidad;
        }
    }
}
