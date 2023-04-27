module ITS291FS.SwaggerJson

[<Literal>]
let swaggerJson = """{
    "openapi": "3.0.1",
    "info": {
        "title": "ITS291FS",
        "version": "1.0"
    },
    "paths": {
        "/users/list": {
            "get": {
                "tags": [
                    "ITS291FS"
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        },
        "/users": {
            "post": {
                "tags": [
                    "ITS291FS"
                ],
                "requestBody": {
                    "content": {
                        "application/json": {
                            "schema": {
                                "$ref": "#/components/schemas/UserPost"
                            }
                        }
                    }
                },
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        },
        "/{username}": {
            "delete": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            },
            "get": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        },
        "/{username}/accountBalance": {
            "put": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    },
                    {
                        "name": "op",
                        "in": "query",
                        "style": "form",
                        "schema": {
                            "type": "string"
                        }
                    },
                    {
                        "name": "amount",
                        "in": "query",
                        "required": true,
                        "style": "form",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        },
        "/{username}/items": {
            "get": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            },
            "post": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "requestBody": {
                    "content": {
                        "application/json": {
                            "schema": {
                                "$ref": "#/components/schemas/ItemPost"
                            }
                        }
                    }
                },
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        },
        "/{username}/items/{name}": {
            "delete": {
                "tags": [
                    "ITS291FS"
                ],
                "parameters": [
                    {
                        "name": "username",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    },
                    {
                        "name": "name",
                        "in": "path",
                        "required": true,
                        "style": "simple",
                        "schema": {
                            "type": "string"
                        }
                    }
                ],
                "responses": {
                    "200": {
                        "description": "Success"
                    }
                }
            }
        }
    },
    "components": {
        "schemas": {
            "ItemPost": {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "nullable": true
                    },
                    "price": {
                        "type": "number",
                        "format": "double"
                    }
                },
                "additionalProperties": false
            },
            "UserPost": {
                "type": "object",
                "properties": {
                    "username": {
                        "type": "string",
                        "nullable": true
                    },
                    "password": {
                        "type": "string",
                        "nullable": true
                    },
                    "account_balance": {
                        "type": "number",
                        "format": "double"
                    }
                },
                "additionalProperties": false
            }
        }
    }
}"""
