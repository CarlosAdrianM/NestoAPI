using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace NestoAPI.Models.Clientes
{
    public class Iban
    {
        public Iban(string codigo) {
            Codigo = codigo;
        }

        private const string ESPECIALES = "[^0-9A-Za-z]";
        private string _codigo;
        public string Codigo {
            get { return _codigo; }
            set
            {
                _codigo = value;
                _codigo = _codigo.Trim();
                _codigo.Replace(" ", "");
                _codigo = Regex.Replace(_codigo, ESPECIALES, "", RegexOptions.None);
                _codigo = _codigo.ToUpper();
            }
        }
        public bool EsValido {
            get {
                var iban = Codigo;
                if (iban.Length < 4 || iban[0] == ' ' || iban[1] == ' ' || iban[2] == ' ' || iban[3] == ' ') throw new InvalidOperationException();

                var checksum = 0;
                var ibanLength = iban.Length;
                for (int charIndex = 0; charIndex < ibanLength; charIndex++)
                {
                    if (iban[charIndex] == ' ') continue;



                    int value;
                    var c = iban[(charIndex + 4) % ibanLength];
                    if ((c >= '0') && (c <= '9'))
                    {
                        value = c - '0';
                    }
                    else if ((c >= 'A') && (c <= 'Z'))
                    {
                        value = c - 'A';
                        checksum = (checksum * 10 + (value / 10 + 1)) % 97;
                        value %= 10;
                    }
                    else if ((c >= 'a') && (c <= 'z'))
                    {
                        value = c - 'a';
                        checksum = (checksum * 10 + (value / 10 + 1)) % 97;
                        value %= 10;
                    }
                    else throw new InvalidOperationException();

                    checksum = (checksum * 10 + value) % 97;

                }
                return checksum == 1;

            }
        }

        public string Formateado
        {
            get
            {
                if (Codigo.Length == 24)
                {
                    return Codigo.Substring(0, 4) + " " +
                    Codigo.Substring(4, 4) + " " +
                    Codigo.Substring(8, 4) + " " +
                    Codigo.Substring(12, 4) + " " +
                    Codigo.Substring(16, 4) + " " +
                    Codigo.Substring(20, 4);
                }

                string formateado="";
                string resto = "";
                if (Codigo.Length < 24)
                {
                    formateado = Codigo + string.Concat(Enumerable.Repeat("X", 24 - Codigo.Length));
                }

                if (Codigo.Length > 24)
                {
                    formateado = Codigo.Substring(0,24);
                    resto = " +(" + Codigo.Substring(24) + ")";
                }


                return formateado.Substring(0, 4) + " " +
                    formateado.Substring(4, 4) + " " +
                    formateado.Substring(8, 4) + " " +
                    formateado.Substring(12, 4) + " " +
                    formateado.Substring(16, 4) + " " +
                    formateado.Substring(20, 4) 
                    + resto;
            }
        }
    }
}