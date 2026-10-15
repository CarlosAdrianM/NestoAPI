using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Outlet
{
    /// <summary>
    /// NestoAPI#643: el cuadre del Outlet. Calcula (con <see cref="ReglasOutlet"/>) qué productos entran, salen o
    /// cambian de grupo y, solo con el interruptor <see cref="CLAVE_INTERRUPTOR"/> encendido, lo aplica en
    /// ProductosCategoriasSecundarias y republica por Nesto_sync, en tandas.
    /// </summary>
    public class ServicioOutlet
    {
        /// <summary>
        /// Interruptor en Web.config. APAGADO por defecto (falta la clave o no dice «true»): solo dry-run. Se enciende
        /// en el Web.config DEL REPO (el deploy pisa el del servidor; ver reference_flags_web_config_se_pierden_al_publicar).
        /// </summary>
        public const string CLAVE_INTERRUPTOR = "Outlet:SincronizarConEstado";

        /// <summary>Usuario de auditoría de las filas que escribe el cuadre y de los encolados en Nesto_sync.</summary>
        public const string USUARIO = "Outlet automático";

        /// <summary>Productos por tanda: se escriben y se encolan juntos antes de pasar a la siguiente.</summary>
        internal const int TAMANO_TANDA = 100;

        private readonly IRepositorioOutlet repositorio;
        private readonly Func<string, string> leerParametro;
        private readonly Action<Exception> registrarError;

        public ServicioOutlet() : this(null)
        {
        }

        internal ServicioOutlet(IRepositorioOutlet repositorio, Func<string, string> leerParametro = null, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio ?? new RepositorioOutletSql();
            this.leerParametro = leerParametro ?? (clave => ConfigurationManager.AppSettings[clave]);
            this.registrarError = registrarError ?? ElmahHelper.Log;
        }

        public bool InterruptorEncendido()
        {
            return bool.TryParse(leerParametro(CLAVE_INTERRUPTOR)?.Trim(), out bool encendido) && encendido;
        }

        /// <summary>Lo que haría el cuadre ahora mismo, sin escribir nada.</summary>
        public async Task<ResultadoCuadreOutlet> Previsualizar()
        {
            List<SituacionOutletProducto> situacion = await repositorio.CargarSituacion().ConfigureAwait(false);
            return Resumir(situacion, ReglasOutlet.Calcular(situacion), aplicado: false);
        }

        /// <summary>
        /// El cuadre del job: con el interruptor apagado es un dry-run (igual que <see cref="Previsualizar"/>); con él
        /// encendido escribe cada producto en su transacción y encola en Nesto_sync los que tienen ficha web, tanda a
        /// tanda. Un producto que falla no para a los demás (va a ELMAH y se cuenta).
        /// </summary>
        public async Task<ResultadoCuadreOutlet> Cuadrar()
        {
            List<SituacionOutletProducto> situacion = await repositorio.CargarSituacion().ConfigureAwait(false);
            List<CambioOutlet> cambios = ReglasOutlet.Calcular(situacion);
            if (!InterruptorEncendido())
            {
                return Resumir(situacion, cambios, aplicado: false);
            }

            ResultadoCuadreOutlet resultado = Resumir(situacion, cambios, aplicado: true);
            for (int inicio = 0; inicio < cambios.Count; inicio += TAMANO_TANDA)
            {
                List<CambioOutlet> tanda = cambios.Skip(inicio).Take(TAMANO_TANDA).ToList();
                List<string> aRepublicar = new List<string>();
                foreach (CambioOutlet cambio in tanda)
                {
                    try
                    {
                        await repositorio.AplicarCambio(cambio, USUARIO).ConfigureAwait(false);
                        resultado.Aplicados++;
                        if (cambio.Republicar)
                        {
                            aRepublicar.Add(cambio.Numero);
                        }
                    }
                    catch (Exception ex)
                    {
                        resultado.ConError++;
                        registrarError(new Exception($"[Outlet #643] No se ha podido aplicar «{cambio.Tipo}» al producto {cambio.Numero}: {ex.Message}", ex));
                    }
                }
                if (aRepublicar.Count > 0)
                {
                    try
                    {
                        resultado.Encolados += await repositorio.EncolarRepublicacion(aRepublicar, USUARIO).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        registrarError(new Exception($"[Outlet #643] Aplicados pero sin encolar en Nesto_sync ({aRepublicar.Count} productos: {string.Join(", ", aRepublicar)}): {ex.Message}", ex));
                    }
                }
            }
            return resultado;
        }

        internal static ResultadoCuadreOutlet Resumir(List<SituacionOutletProducto> situacion, List<CambioOutlet> cambios, bool aplicado)
        {
            List<SituacionOutletProducto> enOutlet = situacion.Where(ReglasOutlet.DebeEstarEnOutlet).ToList();
            return new ResultadoCuadreOutlet
            {
                Aplicado = aplicado,
                ProductosEnOutletTrasCuadre = enOutlet.Count,
                ProductosConCategoriaOutletHoy = situacion.Count(s => s.CategoriasOutletActuales != null && s.CategoriasOutletActuales.Any()),
                Entran = cambios.Count(c => c.Tipo == TiposCambioOutlet.ENTRA),
                Salen = cambios.Count(c => c.Tipo == TiposCambioOutlet.SALE),
                CambianDeGrupo = cambios.Count(c => c.Tipo == TiposCambioOutlet.CAMBIA_DE_GRUPO),
                SeRepublicarian = cambios.Count(c => c.Republicar),
                Estado4SinTienda = situacion.Count(s => s.Estado == ReglasOutlet.ESTADO_OUTLET && s.TieneFichaWeb && !s.Publicable),
                Cambios = cambios
            };
        }
    }

    /// <summary>NestoAPI#643: lo que devuelve la previsualización y lo que resume el correo del job.</summary>
    public class ResultadoCuadreOutlet
    {
        /// <summary>true si se ha escrito (interruptor encendido); false si es un dry-run.</summary>
        public bool Aplicado { get; set; }

        /// <summary>Cómo está el interruptor Outlet:SincronizarConEstado (lo rellena la previsualización).</summary>
        public bool InterruptorEncendido { get; set; }

        public int ProductosConCategoriaOutletHoy { get; set; }
        public int ProductosEnOutletTrasCuadre { get; set; }
        public int Entran { get; set; }
        public int Salen { get; set; }
        public int CambianDeGrupo { get; set; }

        /// <summary>Cambios de productos con ficha web (los que se encolan en Nesto_sync al aplicar).</summary>
        public int SeRepublicarian { get; set; }

        /// <summary>De estado 4 con ficha web que la puerta no deja ir a la tienda (no entran).</summary>
        public int Estado4SinTienda { get; set; }

        public int Aplicados { get; set; }
        public int ConError { get; set; }
        public int Encolados { get; set; }

        public List<CambioOutlet> Cambios { get; set; } = new List<CambioOutlet>();
    }
}
