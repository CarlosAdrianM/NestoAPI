using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Depositos
{
    public class DatosCorreoDeposito
    {
        private const int MAX_DIAS_SIN_ROJO = 15;

        public string Nombre { get; set; }
        public string Enlace { get; set; }
        public DateTime FechaPrimerMovimiento { get; set; }
        public string Imagen { get; set; }
        public string ProductoId { get; set; }
        public int UnidadesDevueltas { get; set; }
        public int UnidadesEnviadasProveedor { get; set; }
        public int UnidadesReservadas { get; set; }
        public int UnidadesStock { get; set; }
        public int UnidadesVendidas { get; set; } // para el cálculo de cuando reponer

       


        public string ColorTexto { 
            get {
                if (ColorFondo == "#FFFFFF")
                {
                    return "#000000"; // negro
                }
                else
                {
                    return "#FFFFFF"; // blanco
                }
            } 
        }

        public string ColorFondo
        {
            get
            {
                if ((DiasStock > 120 && DiasAntiguedad > MAX_DIAS_SIN_ROJO) || UnidadesEnviadasProveedor < 0 || (DiasAntiguedad > MAX_DIAS_SIN_ROJO && TotalUnidadesVendidas == 0))
                {
                    return "#FF0000"; // rojo -> retirar
                }
                else if (DiasStock < 7 && TotalUnidadesVendidas > 0)
                {
                    return "#008000"; // verde -> reponer
                }
                else
                {
                    return "#FFFFFF"; // blanco -> Esperar
                }
            }
        }
        public int DiasAntiguedad { 
            get
            {
                int dias = (int)(DateTime.Today - FechaPrimerMovimiento).TotalDays;
                if (dias == 0)
                {
                    dias = 1;
                }
                return dias > Constantes.Productos.DEPOSITO_DIAS_ESTADISTICA ? Constantes.Productos.DEPOSITO_DIAS_ESTADISTICA : dias;
            } 
        }
        public int DiasStock
        {
            get
            {
                if (VentaDiaria == 0)
                {
                    return 0;
                }
                return (int)Decimal.Divide(UnidadesStock, VentaDiaria);
            }
        }
        public int TotalUnidadesVendidas
        {
            get
            {
                return UnidadesVendidas - UnidadesDevueltas + UnidadesReservadas;
            }
        }
        public int UnidadesPendientesFacturar { 
            get {
                int cantidad = UnidadesEnviadasProveedor - UnidadesStock + UnidadesReservadas;
                if (cantidad > UnidadesEnviadasProveedor)
                {
                    cantidad = UnidadesEnviadasProveedor;
                }
                return cantidad < 0 ? 0 : cantidad;
            } 
        }
        public decimal VentaDiaria
        {
            get
            {
                if (DiasAntiguedad == 0)
                {
                    return 0;
                }
                return Decimal.Divide(TotalUnidadesVendidas, DiasAntiguedad);
            }
        }

        public int UnidadesRetenidasFaltaStock
        {
            get
            {
                if (UnidadesPendientesFacturar == UnidadesEnviadasProveedor - UnidadesStock + UnidadesReservadas)
                {
                    return 0;
                }
                return UnidadesEnviadasProveedor - UnidadesStock + UnidadesReservadas - UnidadesPendientesFacturar;
            }
        }
    }
}