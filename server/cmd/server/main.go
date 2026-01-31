package main

import (
	"database/sql"
	"fmt"
	"log"
	"net/http"

	_ "github.com/go-sql-driver/mysql"
)

func handler(w http.ResponseWriter, r *http.Request) {
	fmt.Fprintf(w, "Hi there, I love %s!", r.URL.Path[1:])
}

func pingHandler(w http.ResponseWriter, r *http.Request) {

}

func main() {
	db, err := sql.Open("mysql", "root@tcp(localhost:3306)/")
	if err != nil {
		log.Fatal(err)
	}
	defer db.Close()

	http.HandleFunc("/", handler)
	http.HandleFunc("/ping", func(w http.ResponseWriter, r *http.Request) {
		err = db.Ping()
		if err != nil {
			http.Error(w, "DB connection error", http.StatusInternalServerError)
			return
		}
		fmt.Println("success!!")
	})
	log.Fatal(http.ListenAndServe(":8080", nil))
}
