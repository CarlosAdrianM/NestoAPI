using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// NestoAPI#493: freno por zonas para arrancar una agencia "de menos a más" (lo pidió CTT al dar
    /// las credenciales de producción). Un parámetro de usuario (empresa 1, «(defecto)») con la lista
    /// de zonas en las que la agencia compite: p. ej. "Provincial" la primera semana, luego
    /// "Provincial, Peninsular", y al final vacío (= todas). Fuera de esas zonas la tarifa no cubre y
    /// el envío sigue saliendo por la siguiente más barata. Cada fase es cambiar el parámetro, sin
    /// publicar nada. El job de la comparativa sombra NO lo aplica: mide el potencial completo.
    /// </summary>
    public static class ZonasActivasAgencia
    {
        /// <summary>Clave del parámetro para CTT. Valor: nombres de <see cref="ZonasEnvioAgencia"/> separados por comas; vacío o ausente = todas.</summary>
        public const string CLAVE_CTT = "CTTZonasActivas";

        /// <summary>null = sin freno (todas las zonas). Nombres desconocidos se ignoran.</summary>
        public static ISet<ZonasEnvioAgencia> Parsear(string valorParametro)
        {
            if (string.IsNullOrWhiteSpace(valorParametro))
            {
                return null;
            }
            var zonas = new HashSet<ZonasEnvioAgencia>();
            foreach (string nombre in valorParametro.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                if (Enum.TryParse(nombre, ignoreCase: true, result: out ZonasEnvioAgencia zona))
                {
                    zonas.Add(zona);
                }
            }
            return zonas;
        }

        public static bool EstaActiva(ISet<ZonasEnvioAgencia> zonasActivas, ZonasEnvioAgencia zona)
            => zonasActivas == null || zonasActivas.Contains(zona);

        /// <summary>Lee el parámetro de CTT de la BD (empresa por defecto, usuario «(defecto)»).</summary>
        public static string LeerValorCTT(NVEntities db)
        {
            ParametroUsuario parametro = db.ParametrosUsuario.FirstOrDefault(p =>
                p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && p.Usuario == "(defecto)"
                && p.Clave == CLAVE_CTT);
            return parametro?.Valor;
        }
    }

    /// <summary>Envuelve una tarifa y la deja sin cobertura (coste MaxValue) fuera de las zonas activas.</summary>
    public class TarifaConZonasActivas : ITarifaAgencia
    {
        private readonly ITarifaAgencia _interior;
        private readonly ISet<ZonasEnvioAgencia> _zonasActivas;

        public TarifaConZonasActivas(ITarifaAgencia interior, ISet<ZonasEnvioAgencia> zonasActivas)
        {
            _interior = interior ?? throw new ArgumentNullException(nameof(interior));
            _zonasActivas = zonasActivas;
        }

        /// <summary>La tarifa decorada (para mirar sus capacidades, <see cref="CapacidadesTarifa"/>).</summary>
        public ITarifaAgencia Interior => _interior;

        public int AgenciaId => _interior.AgenciaId;
        public byte ServicioId => _interior.ServicioId;
        public string NombreServicio => _interior.NombreServicio;
        public byte HorarioDefectoId => _interior.HorarioDefectoId;

        public decimal CalcularCoste(string codigoPostal, string paisIso, decimal peso, decimal reembolso, decimal recargoCombustible)
        {
            ZonasEnvioAgencia zona = TarifaNacionalBase.ZonaNacional(codigoPostal, paisIso);
            return ZonasActivasAgencia.EstaActiva(_zonasActivas, zona)
                ? _interior.CalcularCoste(codigoPostal, paisIso, peso, reembolso, recargoCombustible)
                : decimal.MaxValue;
        }
    }

    /// <summary>Registro que aplica el freno por zonas a las tarifas de UNA agencia; el resto pasa tal cual.</summary>
    public class RegistroTarifasConZonasActivas : IRegistroTarifas
    {
        private readonly IRegistroTarifas _registro;
        private readonly int _agenciaId;
        private readonly ISet<ZonasEnvioAgencia> _zonasActivas;

        public RegistroTarifasConZonasActivas(IRegistroTarifas registro, int agenciaId, ISet<ZonasEnvioAgencia> zonasActivas)
        {
            _registro = registro ?? throw new ArgumentNullException(nameof(registro));
            _agenciaId = agenciaId;
            _zonasActivas = zonasActivas;
        }

        public IEnumerable<ITarifaAgencia> Todas()
            => _zonasActivas == null
                ? _registro.Todas()
                : _registro.Todas().Select(t => t.AgenciaId == _agenciaId ? new TarifaConZonasActivas(t, _zonasActivas) : t);
    }

    /// <summary>
    /// Único sitio que compone el comparador para SELECCIONAR agencia (MasEconomica, Coste, cobertura
    /// al crear la etiqueta): agencias dadas de alta, sombras excluidas de la elección y freno por
    /// zonas de CTT. Antes estaba copiado en tres controladores.
    /// </summary>
    public static class ComparadorAgenciasFactory
    {
        public static ComparadorAgencias ParaSeleccion(NVEntities db)
        {
            var numerosExistentes = db.AgenciasTransportes.Select(a => a.Numero).Distinct().ToList();
            var idsSombra = db.AgenciasTransportes.Where(a => a.EsSombra).Select(a => a.Numero).ToList();
            IRegistroTarifas registro = new RegistroTarifasExistentes(new RegistroTarifas(), numerosExistentes);
            registro = new RegistroTarifasConZonasActivas(registro, Constantes.Agencias.AGENCIA_CTT,
                ZonasActivasAgencia.Parsear(ZonasActivasAgencia.LeerValorCTT(db)));
            return new ComparadorAgencias(registro, new ProveedorRecargoCombustibleEF(db), idsSombra);
        }
    }
}
